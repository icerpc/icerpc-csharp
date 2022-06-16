// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Security;

namespace IceRpc.Internal;

/// <summary>Code common to client and server connections.</summary>
internal sealed class ConnectionCore
{
    internal NetworkConnectionInformation? NetworkConnectionInformation { get; private set; }

    private bool _isClosing;
    private bool _isClosed;

    // The mutex protects mutable data members and ensures the logic for some operations is performed atomically.
    private readonly object _mutex = new();

    private Action<IConnection, Exception>? _onClose;

    // TODO: replace this field by individual fields
    private readonly ConnectionOptions _options;

    private IProtocolConnection? _protocolConnection;

    private readonly CancellationTokenSource _shutdownCancellationSource = new();

    // The shutdown task is assigned when shutdown is initiated.
    private Task? _shutdownTask;

    internal ConnectionCore(ConnectionOptions options) => _options = options;

    /// <summary>Aborts the connection.</summary>
    internal void Abort(IConnection connection) => Close(connection, new ConnectionAbortedException());

    /// <summary>Establishes a connection.</summary>
    /// <param name="connection">The connection being connected.</param>
    /// <param name="isServer">When true, the connection is a server connection; when false, it's a client connection.
    /// </param>
    /// <param name="networkConnection">The underlying network connection.</param>
    /// <param name="protocolConnectionFactory">The protocol connection factory.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal async Task ConnectAsync<T>(
        IConnection connection,
        bool isServer,
        T networkConnection,
        IProtocolConnectionFactory<T> protocolConnectionFactory,
        CancellationToken cancel) where T : INetworkConnection
    {
        // Create the protocol connection. The protocol connection owns the network connection and is responsible
        // for its disposal. If the protocol connection establishment fails, the network connection is disposed.
        IProtocolConnection protocolConnection = protocolConnectionFactory.CreateConnection(
            networkConnection,
            _options);

        try
        {
            NetworkConnectionInformation = await protocolConnection.ConnectAsync(
                isServer,
                onIdle: () => _ = ShutdownAsync(connection, "idle connection", CancellationToken.None),
                onShutdown: message => _ = ShutdownAsync(connection, message, CancellationToken.None),
                cancel).ConfigureAwait(false);
        }
        catch
        {
            protocolConnection.Abort(new ConnectionClosedException());
            throw;
        }

        try
        {
            lock (_mutex)
            {
                if (_shutdownTask != null || _isClosed)
                {
                    return;
                }

                _protocolConnection = protocolConnection;

                // Start accepting requests. _protocolConnection might be updated before the task is ran so it's important
                // to use protocolConnection here.
                _ = Task.Run(
                    async () =>
                    {
                        Exception? exception = null;
                        try
                        {
                            await protocolConnection.AcceptRequestsAsync(connection).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            exception = ex;
                        }
                        finally
                        {
                            Close(connection, exception);
                        }
                    },
                    cancel);
            }
        }
        catch (Exception exception)
        {
            Close(connection, exception);
            throw;
        }
    }

    internal async Task<IncomingResponse> InvokeAsync(
        IConnection connection,
        OutgoingRequest request,
        CancellationToken cancel)
    {
        IProtocolConnection protocolConnection = GetProtocolConnection() ?? throw new ConnectionClosedException();

        try
        {
            return await protocolConnection.InvokeAsync(request, connection, cancel).ConfigureAwait(false);
        }
        catch (ConnectionLostException exception)
        {
            // If the network connection is lost while sending the request, we close the connection now instead of
            // waiting for AcceptRequestsAsync to throw. It's necessary to ensure that the next InvokeAsync will
            // fail with ConnectionClosedException and won't be retried on this connection.
            Close(connection, exception);
            throw;
        }
        catch (ConnectionClosedException exception)
        {
            // Ensure that the shutdown is initiated if the invocations fails with ConnectionClosedException. It's
            // possible that the connection didn't receive yet the GoAway message. Initiating the shutdown now
            // ensures that the next InvokeAsync will fail with ConnectionClosedException and won't be retried on
            // this connection.
            _ = ShutdownAsync(connection, exception.Message, cancel);
            throw;
        }

        IProtocolConnection? GetProtocolConnection()
        {
            lock (_mutex)
            {
                return _protocolConnection ?? throw new ConnectionClosedException();
            }
        }
    }

    internal void OnClose(IConnection connection, Action<IConnection, Exception> callback)
    {
        bool executeCallback = false;

        lock (_mutex)
        {
            if (_isClosing)
            {
                executeCallback = true;
            }
            else
            {
                _onClose += callback;
            }
        }

        if (executeCallback)
        {
            callback(connection, new ConnectionClosedException());
        }
    }

    internal async Task ShutdownAsync(IConnection connection, string message, CancellationToken cancel)
    {
        lock (_mutex)
        {
            if (_isClosed)
            {
                return;
            }

            if (_shutdownTask == null && _protocolConnection != null)
            {
                _shutdownTask = ShutdownAsyncCore();
            }
        }

        if (_shutdownTask != null)
        {
            // If the application cancels ShutdownAsync, cancel the shutdown cancellation source to speed up shutdown.
            using CancellationTokenRegistration _ = cancel.Register(() =>
            {
                try
                {
                    _shutdownCancellationSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            });

            // Wait for the shutdown to complete.
            await _shutdownTask.ConfigureAwait(false);
        }
        else
        {
            Close(connection, new ConnectionClosedException());
        }

        async Task ShutdownAsyncCore()
        {
            // Yield before continuing to ensure the code below isn't executed with the mutex locked.
            await Task.Yield();

            InvokeOnClose(connection, exception: null);

            using var cancelCloseTimeoutSource = new CancellationTokenSource();
            _ = CloseOnTimeoutAsync(cancelCloseTimeoutSource.Token);

            Exception? exception = null;
            try
            {
                // Shutdown the connection. If the given cancellation token is canceled, pending invocations and
                // dispatches are canceled to speed up shutdown. Otherwise, the protocol shutdown is canceled on close
                // timeout.
                await _protocolConnection.ShutdownAsync(message, cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                cancelCloseTimeoutSource.Cancel();
                Close(connection, exception);
            }

            async Task CloseOnTimeoutAsync(CancellationToken cancel)
            {
                try
                {
                    await Task.Delay(_options.CloseTimeout, cancel).ConfigureAwait(false);
                    Close(connection, new ConnectionAbortedException("shutdown timed out"));
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    /// <summary>Closes the connection. Resources allocated for the connection are freed.</summary>
    /// <param name="connection">The connection being closed.</param>
    /// <param name="exception">The optional exception responsible for the connection closure. A <c>null</c>
    /// exception indicates a graceful connection closure.</param>
    private void Close(IConnection connection, Exception? exception)
    {
        InvokeOnClose(connection, exception);

        lock (_mutex)
        {
            if (_isClosed)
            {
                return;
            }
            _isClosed = true;

            if (_protocolConnection != null)
            {
                _protocolConnection.Abort(exception ?? new ConnectionClosedException());
                _protocolConnection = null;
            }

            // Time to get rid of disposable resources
            _shutdownCancellationSource.Dispose();
        }
    }

    private void InvokeOnClose(IConnection connection, Exception? exception)
    {
        Action<IConnection, Exception>? onClose;

        lock (_mutex)
        {
            _isClosing = true;

            onClose = _onClose;
            _onClose = null; // clear _onClose because we want to execute it only once
        }

        // Execute callbacks (if any) outside lock
        onClose?.Invoke(connection, exception ?? new ConnectionClosedException());
    }
}
