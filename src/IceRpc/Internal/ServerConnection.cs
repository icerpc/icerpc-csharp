// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc.Internal;

internal class ServerConnection : IConnection, IAsyncDisposable
{
    /// <inheritdoc/>
    public bool IsInvocable => State < ConnectionState.ShuttingDown;

    /// <inheritdoc/>
    public NetworkConnectionInformation? NetworkConnectionInformation { get; private set; }

    /// <inheritdoc/>
    public Protocol Protocol { get; }

    /// <summary>Gets the state of the connection.</summary>
    public ConnectionState State
    {
        get
        {
            lock (_mutex)
            {
                return _state;
            }
        }
    }

    private protected IProtocolConnection? _protocolConnection;

    private readonly CancellationTokenSource _connectCancellationSource = new();

    // The mutex protects mutable data members and ensures the logic for some operations is performed atomically.
    private readonly object _mutex = new();

    private Action<IConnection, Exception>? _onClose;

    // TODO: replace this field by individual fields
    private readonly ConnectionOptions _options;

    private readonly CancellationTokenSource _shutdownCancellationSource = new();

    private ConnectionState _state = ConnectionState.NotConnected;

    // The state task is assigned when the state is updated to Connecting or ShuttingDown. It's completed once the
    // state update completes. It's protected with _mutex.
    private Task? _stateTask;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Perform a speedy graceful shutdown by canceling invocations and dispatches in progress.
        await ShutdownAsync("connection disposed", new CancellationToken(canceled: true)).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        // A proxy can only invoke on a server connection after a request was received on that connection; otherwise,
        // the proxy could not get hold of this connection.
        IProtocolConnection protocolConnection = GetProtocolConnection() ?? throw new ConnectionClosedException();

        try
        {
            return await protocolConnection.InvokeAsync(request, this, cancel).ConfigureAwait(false);
        }
        catch (ConnectionLostException exception)
        {
            // If the network connection is lost while sending the request, we close the connection now instead of
            // waiting for AcceptRequestsAsync to throw. It's necessary to ensure that the next InvokeAsync will
            // fail with ConnectionClosedException and won't be retried on this connection.
            Close(exception, protocolConnection);
            throw;
        }
        catch (ConnectionClosedException exception)
        {
            // Ensure that the shutdown is initiated if the invocations fails with ConnectionClosedException. It's
            // possible that the connection didn't receive yet the GoAway message. Initiating the shutdown now
            // ensures that the next InvokeAsync will fail with ConnectionClosedException and won't be retried on
            // this connection.
            InitiateShutdown(exception.Message);
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

    /// <summary>Constructs a server connection from an accepted network connection.</summary>
    internal ServerConnection(Protocol protocol, ConnectionOptions options)
    {
        Protocol = protocol;
        // TODO: "splat" _options
        _options = options;
        _state = ConnectionState.Connecting;
    }

    /// <summary>Aborts the connection. This methods switches the connection state to <see
    /// cref="ConnectionState.Closed"/>.</summary>
    internal void Abort() => Close(new ConnectionAbortedException());

    /// <summary>Establishes a connection. This method is used for both client and server connections.</summary>
    /// <param name="networkConnection">The underlying network connection.</param>
    /// <param name="protocolConnectionFactory">The protocol connection factory.</param>
    /// <param name="onClose">An action to execute when the connection is closed.</param>
    internal async Task ConnectAsync<T>(
        T networkConnection,
        IProtocolConnectionFactory<T> protocolConnectionFactory,
        Action<IConnection, Exception>? onClose) where T : INetworkConnection
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

            // Create the protocol connection.
            IProtocolConnection protocolConnection = protocolConnectionFactory.CreateConnection(
                networkConnection,
                _options);

            try
            {
                // Connect the protocol connection.
                NetworkConnectionInformation = await protocolConnection.ConnectAsync(
                    isServer: true,
                    cancel).ConfigureAwait(false);
            }
            catch
            {
                protocolConnection.Dispose();
                throw;
            }

            lock (_mutex)
            {
                if (_state == ConnectionState.Connecting)
                {
                    _state = ConnectionState.Active;
                }
                else if (_state == ConnectionState.Closed)
                {
                    // This can occur if the connection is aborted shortly after the connection establishment.
                    protocolConnection.Dispose();
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
                _onClose = onClose;

                // Switch the connection to the ShuttingDown state as soon as the protocol receives a notification
                // that peer initiated shutdown. This is in particular useful for the connection pool to not return
                // a connection which is being shutdown.
                _protocolConnection.OnShutdown = InitiateShutdown;

                // Also initiate shutdown if the protocol connection is idle.
                _protocolConnection.OnIdle = () => InitiateShutdown("idle connection");

                // Start accepting requests. _protocolConnection might be updated before the task is ran so it's
                // important to use protocolConnection here.
                _ = Task.Run(async () =>
                {
                    Exception? exception = null;
                    try
                    {
                        await protocolConnection.AcceptRequestsAsync(this).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                    }
                    finally
                    {
                        Close(exception, protocolConnection);
                    }
                });
            }
        }
        catch (OperationCanceledException) when (connectTimeoutCancellationSource.IsCancellationRequested)
        {
            var exception = new ConnectTimeoutException();
            Close(exception);
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
            Close(exception);
            throw;
        }
    }

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal Task ShutdownAsync(CancellationToken cancel = default) => ShutdownAsync("connection shutdown", cancel);

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="message">The message transmitted to the peer (when using the IceRPC protocol).</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal async Task ShutdownAsync(string message, CancellationToken cancel = default)
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
            Close(new ConnectionClosedException());
        }
    }

    /// <summary>Closes the connection. Resources allocated for the connection are freed.</summary>
    /// <param name="exception">The optional exception responsible for the connection closure. A <c>null</c>
    /// exception indicates a gracefull connection closure.</param>
    /// <param name="protocolConnection">If not <c>null</c>, the connection closure will only be performed if the
    /// protocol connection matches.</param>
    private void Close(Exception? exception, IProtocolConnection? protocolConnection = null)
    {
        try
        {
            _connectCancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        lock (_mutex)
        {
            if (_state == ConnectionState.NotConnected ||
                _state == ConnectionState.Closed ||
                (protocolConnection != null && _protocolConnection != protocolConnection))
            {
                return;
            }

            if (_protocolConnection != null)
            {
                if (exception != null)
                {
                    _protocolConnection.Abort(exception);
                }

                _protocolConnection.Dispose();
                _protocolConnection = null;
            }

            _state = ConnectionState.Closed;
            _stateTask = null;

            if (_state == ConnectionState.Closed)
            {
                // Time to get rid of disposable resources if the connection is in the closed state.
                _shutdownCancellationSource.Dispose();
                _connectCancellationSource.Dispose();
            }
        }

        // Raise the Closed event, this will call user code so we shouldn't hold the mutex.
        if (State == ConnectionState.Closed)
        {
            try
            {
                // TODO: pass a null exception instead? See issue #1100.
                (_onClose + _options?.OnClose)?.Invoke(
                    this,
                    exception ?? new ConnectionClosedException());
            }
            catch
            {
                // Ignore, on close actions shouldn't raise exceptions.
            }
        }
    }

    private void InitiateShutdown(string message)
    {
        lock (_mutex)
        {
            // If the connection is active, switch the state to ShuttingDown and initiate the shutdown.
            if (_state == ConnectionState.Active)
            {
                _state = ConnectionState.ShuttingDown;
                _stateTask = ShutdownAsyncCore(
                    _protocolConnection!,
                    message,
                    _shutdownCancellationSource.Token);
            }
        }
    }

    private async Task ShutdownAsyncCore(
        IProtocolConnection protocolConnection,
        string message,
        CancellationToken cancel)
    {
        // Yield before continuing to ensure the code below isn't executed with the mutex locked and that _stateTask
        // is assigned before any synchronous continuations are ran.
        await Task.Yield();

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
            Close(exception);
        }

        async Task CloseOnTimeoutAsync(CancellationToken cancel)
        {
            try
            {
                await Task.Delay(_options.CloseTimeout, cancel).ConfigureAwait(false);
                Close(new ConnectionAbortedException("shutdown timed out"), protocolConnection);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
