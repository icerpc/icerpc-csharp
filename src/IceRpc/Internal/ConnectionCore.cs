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
    internal bool IsInvocable => State < ConnectionState.ShuttingDown;

    internal NetworkConnectionInformation? NetworkConnectionInformation { get; private set; }

    /// <summary>Gets the state of the connection.</summary>
    internal ConnectionState State
    {
        get
        {
            lock (_mutex)
            {
                return _state;
            }
        }
    }

    private readonly CancellationTokenSource _connectCancellationSource = new();

    // The mutex protects mutable data members and ensures the logic for some operations is performed atomically.
    private readonly object _mutex = new();

    private Action<IConnection, Exception>? _onClose;

    // TODO: replace this field by individual fields
    private readonly ConnectionOptions _options;

    private IProtocolConnection? _protocolConnection;

    private readonly CancellationTokenSource _shutdownCancellationSource = new();

    private ConnectionState _state = ConnectionState.NotConnected;

    // The state task is assigned when the state is updated to Connecting or ShuttingDown. It's completed once the
    // state update completes. It's protected with _mutex.
    private Task? _stateTask;

    internal ConnectionCore(ConnectionOptions options) => _options = options;

    /// <summary>Aborts the connection. This methods switches the connection state to <see
    /// cref="ConnectionState.Closed"/>.</summary>
    internal void Abort(IConnection connection) => Close(connection, new ConnectionAbortedException());

    /// <summary>Establishes a connection.</summary>
    /// <param name="connection">The connection being connected.</param>
    /// <param name="isServer">When true, the connection is a server connection; when false, it's a client connection.
    /// </param>
    /// <param name="networkConnection">The underlying network connection.</param>
    /// <param name="protocolConnectionFactory">The protocol connection factory.</param>
    // TODO: make private
    internal async Task ConnectAsync<T>(
        IConnection connection,
        bool isServer,
        T networkConnection,
        IProtocolConnectionFactory<T> protocolConnectionFactory) where T : INetworkConnection
    {
        using var connectTimeoutCancellationSource = new CancellationTokenSource(_options.ConnectTimeout);
        using var connectCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            connectTimeoutCancellationSource.Token,
            _connectCancellationSource.Token);
        CancellationToken cancel = connectCancellationSource.Token;

        try
        {
            // Make sure we establish the connection asynchronously without holding any mutex lock from the caller.
            await Task.Yield();

            // Create the protocol connection. The protocol connection owns the network connection and is responsible
            // for its disposal. If the protocol connection establishment fails, the network connection is disposed.
            (IProtocolConnection protocolConnection, NetworkConnectionInformation) =
                await protocolConnectionFactory.CreateConnectionAsync(
                    networkConnection,
                    isServer,
                    _options,
                    onIdle: () => InitiateShutdown(connection, "idle connection"),
                    onShutdown: message => InitiateShutdown(connection, message),
                    cancel).ConfigureAwait(false);

            lock (_mutex)
            {
                if (_state == ConnectionState.Connecting)
                {
                    _state = ConnectionState.Active;
                }
                else if (_state == ConnectionState.Closed)
                {
                    // This can occur if the connection is aborted shortly after the connection establishment.
                    throw new ConnectionClosedException("connection aborted");
                }
                else
                {
                    // The state can switch to ShuttingDown if ShutdownAsync is called while the connection
                    // establishment is in progress.
                    Debug.Assert(_state == ConnectionState.ShuttingDown);
                }

                _stateTask = null;
                _protocolConnection = protocolConnection;

                // Start accepting requests. _protocolConnection might be updated before the task is ran so it's
                // important to use protocolConnection here.
                _ = Task.Run(async () =>
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
                });
            }
        }
        catch (OperationCanceledException) when (connectTimeoutCancellationSource.IsCancellationRequested)
        {
            var exception = new ConnectTimeoutException();
            Close(connection, exception);
            throw exception;
        }
        catch (OperationCanceledException) when (_connectCancellationSource.IsCancellationRequested)
        {
            // This occurs when connection establishment is canceled by Close. We just throw
            // ConnectionClosedException here because the connection is already closed and disposed.
            throw new ConnectionClosedException("connection aborted");
        }
        catch (Exception exception)
        {
            Close(connection, exception);
            throw;
        }
    }

    /// <summary>Establishes the client connection.</summary>
    /// <exception cref="ConnectionClosedException">Thrown if the connection is already closed.</exception>
    internal async Task ConnectClientAsync<T>(
        IClientConnection clientConnection,
        IClientTransport<T> clientTransport,
        IProtocolConnectionFactory<T> protocolConnectionFactory,
        LogNetworkConnectionDecoratorFactory<T> logDecoratorFactory,
        ILoggerFactory loggerFactory,
        SslClientAuthenticationOptions? clientAuthenticationOptions,
        CancellationToken cancel) where T : INetworkConnection
    {
        Task? waitTask = null;
        lock (_mutex)
        {
            if (_state == ConnectionState.NotConnected)
            {
                Debug.Assert(_protocolConnection == null);

                // This is the composition root of client Connections, where we install log decorators when logging
                // is enabled.

                ILogger logger = loggerFactory.CreateLogger("IceRpc.Client");

                T networkConnection = clientTransport.CreateConnection(
                    clientConnection.RemoteEndpoint,
                    clientAuthenticationOptions,
                    logger);

                // TODO: log level
                if (logger.IsEnabled(LogLevel.Error))
                {
                    networkConnection = logDecoratorFactory(
                        networkConnection,
                        clientConnection.RemoteEndpoint,
                        isServer: false,
                        logger);

                    protocolConnectionFactory =
                        new LogProtocolConnectionFactoryDecorator<T>(protocolConnectionFactory, logger);

                    OnClose(
                        clientConnection,
                        (connection, exception) =>
                        {
                            if (NetworkConnectionInformation is NetworkConnectionInformation connectionInformation)
                            {
                                using IDisposable scope = logger.StartClientConnectionScope(connectionInformation);
                                logger.LogConnectionClosedReason(exception);
                            }
                        });
                }

                _state = ConnectionState.Connecting;
                _stateTask = ConnectAsync(
                    clientConnection,
                    isServer: false,
                    networkConnection,
                    protocolConnectionFactory);
            }
            else if (_state == ConnectionState.Active)
            {
                return;
            }
            else
            {
                // ShuttingDown or Closed
                throw new ConnectionClosedException();
            }

            Debug.Assert(_stateTask != null);
            waitTask = _stateTask;
        }

        await waitTask.WaitAsync(cancel).ConfigureAwait(false);
    }

    /// <summary>Connects a server connection.</summary>
    internal Task ConnectServerAsync<T>(
        IConnection connection,
        T networkConnection,
        IProtocolConnectionFactory<T> protocolConnectionFactory) where T : INetworkConnection
    {
        lock (_mutex)
        {
            Debug.Assert(_state == ConnectionState.NotConnected);
            _state = ConnectionState.Connecting;

            _stateTask = ConnectAsync(
                connection,
                isServer: true,
                networkConnection,
                protocolConnectionFactory);

            return _stateTask;
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
            InitiateShutdown(connection, exception.Message);
            throw;
        }

        IProtocolConnection? GetProtocolConnection()
        {
            lock (_mutex)
            {
                if (_state == ConnectionState.Active)
                {
                    return _protocolConnection!;
                }
                else if (_state > ConnectionState.Active)
                {
                    throw new ConnectionClosedException();
                }
                else
                {
                    return null;
                }
            }
        }
    }

    internal void OnClose(IConnection connection, Action<IConnection, Exception> callback)
    {
        bool executeCallback = false;

        lock (_mutex)
        {
            if (_state >= ConnectionState.ShuttingDown)
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
        Task? shutdownTask = null;
        Task? connectTask = null;
        lock (_mutex)
        {
            if (_state == ConnectionState.Connecting)
            {
                _state = ConnectionState.ShuttingDown;
                connectTask = _stateTask;
            }
            else if (_state == ConnectionState.Active)
            {
                _state = ConnectionState.ShuttingDown;
                _stateTask = ShutdownAsyncCore(
                    connection,
                    _protocolConnection!,
                    message,
                    _shutdownCancellationSource.Token);
                shutdownTask = _stateTask;
            }
            else if (_state == ConnectionState.ShuttingDown)
            {
                shutdownTask = _stateTask;
            }
            else if (_state == ConnectionState.Closed)
            {
                return;
            }
        }

        if (connectTask != null)
        {
            // Wait for the connection establishment to complete.
            try
            {
                await connectTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore connection establishment failures.
            }

            // Initiate shutdown.
            lock (_mutex)
            {
                if (_state == ConnectionState.Closed)
                {
                    // The connection is already closed and disposed, nothing to do.
                    return;
                }

                Debug.Assert(_state == ConnectionState.ShuttingDown);
                _stateTask = ShutdownAsyncCore(
                    connection,
                    _protocolConnection!,
                    message,
                    _shutdownCancellationSource.Token);
                shutdownTask = _stateTask;
            }
        }

        if (shutdownTask != null)
        {
            // If the application cancels ShutdownAsync, cancel the shutdown cancellation source to speed up
            // shutdown.
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
            await shutdownTask.ConfigureAwait(false);
        }
        else
        {
            Close(connection, new ConnectionClosedException());
        }
    }

    /// <summary>Closes the connection. Resources allocated for the connection are freed.</summary>
    /// <param name="connection">The connection being closed.</param>
    /// <param name="exception">The optional exception responsible for the connection closure. A <c>null</c>
    /// exception indicates a graceful connection closure.</param>
    private void Close(IConnection connection, Exception? exception)
    {
        // Make sure connection establishment is canceled.
        try
        {
            _connectCancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        InvokeOnClose(connection, exception);

        lock (_mutex)
        {
            if (_protocolConnection != null)
            {
                if (exception != null)
                {
                    _protocolConnection.Abort(exception);
                }

                _protocolConnection.Abort(exception ?? new ConnectionClosedException());
                _protocolConnection = null;
            }
            _state = ConnectionState.Closed;
            _stateTask = null;

            // Time to get rid of disposable resources
            _shutdownCancellationSource.Dispose();
            _connectCancellationSource.Dispose();
        }
    }

    private void InitiateShutdown(IConnection connection, string message)
    {
        lock (_mutex)
        {
            // If the connection is active, switch the state to ShuttingDown and initiate the shutdown.
            if (_state == ConnectionState.Active)
            {
                _state = ConnectionState.ShuttingDown;
                _stateTask = ShutdownAsyncCore(
                    connection,
                    _protocolConnection!,
                    message,
                    _shutdownCancellationSource.Token);
            }
        }
    }

    private void InvokeOnClose(IConnection connection, Exception? exception)
    {
        Action<IConnection, Exception>? onClose;

        lock (_mutex)
        {
            if (_state < ConnectionState.ShuttingDown)
            {
                // Subsequent calls to OnClose must execute their callback immediately.
                _state = ConnectionState.ShuttingDown;
            }

            onClose = _onClose;
            _onClose = null; // clear _onClose because we want to execute it only once
        }

        // Execute callbacks (if any) outside lock
        onClose?.Invoke(connection, exception ?? new ConnectionClosedException());
    }

    private async Task ShutdownAsyncCore(
        IConnection connection,
        IProtocolConnection protocolConnection,
        string message,
        CancellationToken cancel)
    {
        // Yield before continuing to ensure the code below isn't executed with the mutex locked and that _stateTask
        // is assigned before any synchronous continuations are ran.
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
            await protocolConnection.ShutdownAsync(message, cancel).ConfigureAwait(false);
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
