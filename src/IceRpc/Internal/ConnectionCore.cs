// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Security;

namespace IceRpc.Internal;

/// <summary>Code common to client and server connections.</summary>
internal class ConnectionCore
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

    private readonly bool _isResumable;

    private readonly bool _isServer;

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

    internal ConnectionCore(ConnectionState state, bool isServer, ConnectionOptions options, bool isResumable)
    {
        _isServer = isServer;
        _isResumable = isResumable;
        _state = state;
        _options = options;
    }

    /// <summary>Aborts the connection. This methods switches the connection state to <see
    /// cref="ConnectionState.Closed"/>.</summary>
    internal void Abort(IConnection connection) =>
        Close(connection, new ConnectionAbortedException(), isResumable: false);

    /// <summary>Establishes a connection. This method is used for both client and server connections.</summary>
    /// <param name="connection">The connection being connected.</param>
    /// <param name="networkConnection">The underlying network connection.</param>
    /// <param name="protocolConnectionFactory">The protocol connection factory.</param>
    /// <param name="onClose">An action to execute when the connection is closed.</param>
    internal async Task ConnectAsync<T>(
        IConnection connection,
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
                    _isServer,
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
                _protocolConnection.OnShutdown = message => InitiateShutdown(connection, message);

                // Also initiate shutdown if the protocol connection is idle.
                _protocolConnection.OnIdle = () => InitiateShutdown(connection, "idle connection");

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
                        Close(connection, exception, isResumable: true, protocolConnection);
                    }
                });
            }
        }
        catch (OperationCanceledException) when (connectTimeoutCancellationSource.IsCancellationRequested)
        {
            var exception = new ConnectTimeoutException();
            Close(connection, exception, isResumable: true);
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
            Close(connection, exception, isResumable: true);
            throw;
        }
    }

    /// <summary>Establishes the client connection.</summary>
    /// <exception cref="ConnectionClosedException">Thrown if the connection is already closed.</exception>
    internal async Task ConnectClientAsync<T>(
        IClientConnection connection,
        IClientTransport<T> clientTransport,
        IProtocolConnectionFactory<T> protocolConnectionFactory,
        LogNetworkConnectionDecoratorFactory<T> logDecoratorFactory,
        ILoggerFactory loggerFactory,
        SslClientAuthenticationOptions? authenticationOptions,
        CancellationToken cancel) where T : INetworkConnection
    {
        // Loop until the connection is active or connection establishment fails.
        while (true)
        {
            Task? waitTask = null;
            lock (_mutex)
            {
                if (_state == ConnectionState.NotConnected)
                {
                    Debug.Assert(_protocolConnection == null);

                    _stateTask = PerformConnectAsync();

                    Debug.Assert(_state == ConnectionState.Connecting);
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

        Task PerformConnectAsync()
        {
            // This is the composition root of client Connections, where we install log decorators when logging is
            // enabled.

            ILogger logger = loggerFactory.CreateLogger("IceRpc.Client");

            T networkConnection = clientTransport.CreateConnection(
                connection.RemoteEndpoint,
                authenticationOptions,
                logger);

            Action<IConnection, Exception>? onClose = null;

            // TODO: log level
            if (logger.IsEnabled(LogLevel.Error))
            {
                networkConnection = logDecoratorFactory(
                    networkConnection,
                    connection.RemoteEndpoint,
                    isServer: false,
                    logger);

                protocolConnectionFactory =
                    new LogProtocolConnectionFactoryDecorator<T>(protocolConnectionFactory, logger);

                onClose = (connection, exception) =>
                {
                    if (NetworkConnectionInformation is NetworkConnectionInformation connectionInformation)
                    {
                        using IDisposable scope = logger.StartClientConnectionScope(connectionInformation);
                        logger.LogConnectionClosedReason(exception);
                    }
                };
            }

            _state = ConnectionState.Connecting;

            return ConnectAsync(connection, networkConnection, protocolConnectionFactory, onClose);
        }
    }

    internal bool HasCompatibleParams(Endpoint remoteEndpoint) =>
        !_isServer &&
        IsInvocable &&
        _protocolConnection is IProtocolConnection protocolConnection &&
        protocolConnection.HasCompatibleParams(remoteEndpoint);

    internal IProtocolConnection? GetProtocolConnection()
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
            Close(connection, exception, isResumable: true, protocolConnection);
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
    }

    internal async Task ShutdownAsync(
        IConnection connection,
        string message,
        bool isResumable,
        CancellationToken cancel)
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
                    isResumable,
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
                    isResumable,
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
            Close(connection, new ConnectionClosedException(), isResumable);
        }
    }

    /// <summary>Closes the connection. Resources allocated for the connection are freed.</summary>
    /// <param name="connection">The connection being closed.</param>
    /// <param name="exception">The optional exception responsible for the connection closure. A <c>null</c>
    /// exception indicates a graceful connection closure.</param>
    /// <param name="isResumable">If <c>true</c> and the connection is resumable, the connection state will be
    /// <see cref="ConnectionState.NotConnected"/> once this operation returns. Otherwise, it will the
    /// <see cref="ConnectionState.Closed"/> terminal state.</param>
    /// <param name="protocolConnection">If not <c>null</c>, the connection closure will only be performed if the
    /// protocol connection matches.</param>
    private void Close(
        IConnection connection,
        Exception? exception,
        bool isResumable,
        IProtocolConnection? protocolConnection = null)
    {
        if (!isResumable)
        {
            // If the closure is triggered by an non-resumable operation, make sure connection establishment is
            // canceled.
            try
            {
                _connectCancellationSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
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

            // A connection can be resumed if it's configured to be resumable and the operation that closed the
            // connection allows it (explicit shutdown or abort don't allow the connection to be resumed).
            _state = _isResumable && isResumable ? ConnectionState.NotConnected : ConnectionState.Closed;
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
                    connection,
                    exception ?? new ConnectionClosedException());
            }
            catch
            {
                // Ignore, on close actions shouldn't raise exceptions.
            }
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
                    isResumable: true,
                    _shutdownCancellationSource.Token);
            }
        }
    }

    private async Task ShutdownAsyncCore(
        IConnection connection,
        IProtocolConnection protocolConnection,
        string message,
        bool isResumable,
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
            Close(connection, exception, isResumable);
        }

        async Task CloseOnTimeoutAsync(CancellationToken cancel)
        {
            try
            {
                await Task.Delay(_options.CloseTimeout, cancel).ConfigureAwait(false);
                Close(connection, new ConnectionAbortedException("shutdown timed out"), isResumable, protocolConnection);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
