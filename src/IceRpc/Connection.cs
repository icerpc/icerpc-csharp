// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Security;

namespace IceRpc
{
    /// <summary>The state of an IceRpc connection.</summary>
    public enum ConnectionState : byte
    {
        /// <summary>The connection is not connected. If will be connected on the first invocation or when <see
        /// cref="Connection.ConnectAsync"/> is called. A connection is in this state after creation or if it's closed
        /// and resumable.</summary>
        NotConnected,
        /// <summary>The connection establishment is in progress.</summary>
        Connecting,
        /// <summary>The connection is active and can send and receive messages.</summary>
        Active,
        /// <summary>The connection is being gracefully shutdown. If the connection is resumable and the shutdown is
        /// initiated by the peer, the connection will switch to the <see cref="ConnectionState.NotConnected"/> state
        /// once the graceful shutdown completes. It will switch to the <see cref="ConnectionState.Closed"/> state
        /// otherwise.</summary>
        ShuttingDown,
        /// <summary>The connection is closed and it can't be resumed.</summary>
        Closed
    }

    /// <summary>Represents a connection used to send and receive requests and responses.</summary>
    public sealed class Connection : IConnection, IAsyncDisposable
    {
        /// <inheritdoc/>
        public Endpoint Endpoint { get; }

        /// <inheritdoc/>
        public bool IsInvocable => State < ConnectionState.ShuttingDown;

        /// <inheritdoc/>
        public NetworkConnectionInformation? NetworkConnectionInformation { get; private set; }

        /// <inheritdoc/>
        public Protocol Protocol => Endpoint.Protocol;

        /// <summary>The state of the connection.</summary>
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

        /// <inheritdoc/>
        public FeatureCollection Features { get; }

#pragma warning disable CA2213 // field that is of IDisposable type, but it is never disposed
        private readonly CancellationTokenSource _connectCancellationSource = new(); // Disposed by Close
#pragma warning restore CA2213

        private readonly bool _isServer;

        // The mutex protects mutable data members and ensures the logic for some operations is performed atomically.
        private readonly object _mutex = new();

        private Action<Connection, Exception>? _onClose;

        // TODO: replace this field by individual fields
        private readonly ConnectionOptions _options;

        private IProtocolConnection? _protocolConnection;

#pragma warning disable CA2213 // field that is of IDisposable type, but it is never disposed
        private readonly CancellationTokenSource _shutdownCancellationSource = new(); // Disposed by Close
#pragma warning restore CA2213

        private ConnectionState _state = ConnectionState.NotConnected;
        // The state task is assigned when the state is updated to Connecting or ShuttingDown. It's completed once the
        // state update completes. It's protected with _mutex.
        private Task? _stateTask;

#pragma warning disable CA2213 // field that is of IDisposable type, but it is never disposed
        // TODO: move to the protocol implementation (#906)
        private Timer? _timer; // Disposed by Close
#pragma warning restore CA2213

        /// <summary>Constructs a client connection.</summary>
        /// <param name="options">The connection options.</param>
        public Connection(ConnectionOptions options)
        {
            Endpoint = options.RemoteEndpoint ??
                throw new ArgumentException(
                    $"{nameof(ConnectionOptions.RemoteEndpoint)} is not set",
                    nameof(options));

            Features = new FeatureCollection(options.Features);

            // At this point, we consider options to be read-only.
            // TODO: replace _options by "splatted" properties.
            _options = options;
        }

        /// <summary>Constructs a client connection with the specified remote endpoint and  authentication options.
        /// All other properties have their default values.</summary>
        /// <param name="endpoint">The connection remote endpoint.</param>
        /// <param name="authenticationOptions">The client authentication options.</param>
        public Connection(Endpoint endpoint, SslClientAuthenticationOptions? authenticationOptions = null)
            : this(new ConnectionOptions
            {
                AuthenticationOptions = authenticationOptions,
                RemoteEndpoint = endpoint
            })
        {
        }

        /// <summary>Aborts the connection. This methods switches the connection state to <see
        /// cref="ConnectionState.Closed"/>.</summary>
        public void Abort() => Close(new ConnectionAbortedException(), isResumable: false);

        /// <summary>Establishes the connection.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A task that indicates the completion of the connect operation.</returns>
        /// <exception cref="ConnectionClosedException">Thrown if the connection is already closed.</exception>
        public async Task ConnectAsync(CancellationToken cancel = default)
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

                        _stateTask = Endpoint.Protocol == Protocol.Ice ?
                            PerformConnectAsync(
                                _options.IceClientOptions?.ClientTransport ?? IceClientOptions.DefaultClientTransport,
                                _options.IceClientOptions,
                                IceProtocol.Instance.ProtocolConnectionFactory,
                                LogSimpleNetworkConnectionDecorator.Decorate) :
                            PerformConnectAsync(
                                _options.IceRpcClientOptions?.ClientTransport ??
                                    IceRpcClientOptions.DefaultClientTransport,
                                _options.IceRpcClientOptions,
                                IceRpcProtocol.Instance.ProtocolConnectionFactory,
                            LogMultiplexedNetworkConnectionDecorator.Decorate);

                        Debug.Assert(_state == ConnectionState.Connecting);
                    }
                    else if (_state == ConnectionState.Active)
                    {
                        return;
                    }
                    else // ShuttingDown or Closed
                    {
                        throw new ConnectionClosedException();
                    }

                    Debug.Assert(_stateTask != null);
                    waitTask = _stateTask;
                }

                await waitTask.WaitAsync(cancel).ConfigureAwait(false);
            }

            Task PerformConnectAsync<T, TOptions>(
                IClientTransport<T> clientTransport,
                TOptions? protocolOptions,
                IProtocolConnectionFactory<T, TOptions> protocolConnectionFactory,
                LogNetworkConnectionDecoratorFactory<T> logDecoratorFactory)
                    where T : INetworkConnection
                    where TOptions : class
            {
                // This is the composition root of client Connections, where we install log decorators when logging is
                // enabled.

                ILogger logger = _options.LoggerFactory.CreateLogger("IceRpc.Client");

                T networkConnection = clientTransport.CreateConnection(
                    Endpoint,
                    _options.AuthenticationOptions,
                    logger);

                Action<Connection, Exception>? onClose = null;

                if (logger.IsEnabled(LogLevel.Error)) // TODO: log level
                {
                    networkConnection = logDecoratorFactory(networkConnection, Endpoint, isServer: false, logger);

                    protocolConnectionFactory =
                        new LogProtocolConnectionFactoryDecorator<T, TOptions>(protocolConnectionFactory, logger);

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

                return ConnectAsync(networkConnection, protocolOptions, protocolConnectionFactory, onClose);
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() =>
            // Perform a speedy graceful shutdown by canceling invocations and dispatches in progress.
            new(ShutdownAsync("connection disposed", new CancellationToken(canceled: true)));

        /// <summary>Checks if the parameters of the provided endpoint are compatible with this connection. Compatible
        /// means a client could reuse this connection instead of establishing a new connection.</summary>
        /// <param name="remoteEndpoint">The endpoint to check.</param>
        /// <returns><c>true</c> when this connection is an active client connection whose parameters are compatible
        /// with the parameters of the provided endpoint; otherwise, <c>false</c>.</returns>
        /// <remarks>This method checks only the parameters of the endpoint; it does not check other properties.
        /// </remarks>
        public bool HasCompatibleParams(Endpoint remoteEndpoint)
        {
            lock (_mutex)
            {
                return !_isServer &&
                    State == ConnectionState.Active &&
                    _protocolConnection!.HasCompatibleParams(remoteEndpoint);
            }
        }

        /// <inheritdoc/>
        public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            IProtocolConnection? protocolConnection = GetProtocolConnection();
            if (protocolConnection == null)
            {
                await ConnectAsync(cancel).ConfigureAwait(false);
            }
            protocolConnection ??= GetProtocolConnection() ?? throw new ConnectionClosedException();

            try
            {
                return await protocolConnection.InvokeAsync(request, this, cancel).ConfigureAwait(false);
            }
            catch (ConnectionLostException exception)
            {
                // If the network connection is lost while sending the request, we close the connection now instead of
                // waiting for AcceptRequestsAsync to throw. It's necessary to ensure that the next InvokeAsync will
                // fail with ConnectionClosedException and won't be retried on this connection.
                Close(exception, isResumable: true, protocolConnection);
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
                    else if (_state > ConnectionState.Active && !_options.IsResumable)
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

        /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
        /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public Task ShutdownAsync(CancellationToken cancel = default) => ShutdownAsync("connection shutdown", cancel);

        /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
        /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
        /// <param name="message">The message transmitted to the peer (when using the IceRPC protocol).</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public Task ShutdownAsync(string message, CancellationToken cancel = default) =>
            ShutdownAsync(message, isResumable: false, cancel);

        /// <inheritdoc/>
        public override string ToString() => Endpoint.ToString();

        /// <summary>Constructs a server connection from an accepted network connection.</summary>
        internal Connection(Endpoint endpoint, ConnectionOptions options)
        {
            _isServer = true;
            Endpoint = endpoint;
            Features = new FeatureCollection(options.Features);

            // TODO: "splat" _options
            _options = options;
            _state = ConnectionState.Connecting;
        }

        /// <summary>Establishes a connection. This method is used for both client and server connections.</summary>
        /// <param name="networkConnection">The underlying network connection.</param>
        /// <param name="protocolOptions">The protocol options for the new connection.</param>
        /// <param name="protocolConnectionFactory">The protocol connection factory.</param>
        /// <param name="onClose">An action to execute when the connection is closed.</param>
        internal async Task ConnectAsync<T, TOptions>(
            T networkConnection,
            TOptions? protocolOptions,
            IProtocolConnectionFactory<T, TOptions> protocolConnectionFactory,
            Action<Connection, Exception>? onClose)
                where T : INetworkConnection
                where TOptions : class
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

                // Establish the network connection.
                NetworkConnectionInformation = await networkConnection.ConnectAsync(cancel).ConfigureAwait(false);

                // Create the protocol connection.
                IProtocolConnection protocolConnection = await protocolConnectionFactory.CreateProtocolConnectionAsync(
                    networkConnection,
                    NetworkConnectionInformation.Value,
                    _options.Dispatcher,
                    _isServer,
                    protocolOptions,
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
                    _protocolConnection.PeerShutdownInitiated = InitiateShutdown;

                    // Setup a timer to check for the connection idle time every IdleTimeout / 2 period. If the
                    // transport doesn't support idle timeout (e.g.: the colocated transport), IdleTimeout will be
                    // infinite.
                    TimeSpan idleTimeout = NetworkConnectionInformation!.Value.IdleTimeout;
                    if (idleTimeout != TimeSpan.MaxValue && idleTimeout != Timeout.InfiniteTimeSpan)
                    {
                        _timer = new Timer(
                            value => Monitor(_options.KeepAlive),
                            null,
                            idleTimeout / 2,
                            idleTimeout / 2);
                    }

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
                                Close(exception, isResumable: true, protocolConnection);
                            }
                        });
                }
            }
            catch (OperationCanceledException) when (connectTimeoutCancellationSource.IsCancellationRequested)
            {
                var exception = new ConnectTimeoutException();
                Close(exception, isResumable: true);
                throw exception;
            }
            catch (OperationCanceledException) when (_connectCancellationSource.IsCancellationRequested)
            {
                // This occurs when connect is canceled by Abort. We just throw ConnectionClosedException here because
                // the connection is already closed and disposed.
                Debug.Assert(State == ConnectionState.Closed);
                throw new ConnectionClosedException("connection aborted");
            }
            catch (Exception exception)
            {
                Close(exception, isResumable: true);
                throw;
            }
        }

        internal void Monitor(bool keepAlive)
        {
            lock (_mutex)
            {
                if (_state != ConnectionState.Active)
                {
                    return;
                }

                Debug.Assert(_protocolConnection != null && NetworkConnectionInformation != null);

                TimeSpan idleTime =
                    TimeSpan.FromMilliseconds(Environment.TickCount64) - _protocolConnection!.LastActivity;
                if (idleTime > NetworkConnectionInformation.Value.IdleTimeout)
                {
                    if (_protocolConnection.HasInvocationsInProgress)
                    {
                        // Close the connection if we didn't receive a heartbeat and the connection is idle. The server
                        // is supposed to send heartbeats when dispatches are in progress. Close can't be called from
                        // within the synchronization since it calls the "on close" callbacks so we call it from a
                        // thread pool thread.
                        IProtocolConnection protocolConnection = _protocolConnection;
                        Task.Run(() => Close(
                            new ConnectionAbortedException("connection timed out"),
                            isResumable: true,
                            protocolConnection));
                    }
                    else
                    {
                        // The connection is idle, gracefully shut it down.
                        _ = ShutdownAsync("connection idle", isResumable: true, CancellationToken.None);
                    }
                }
                else if (idleTime > NetworkConnectionInformation.Value.IdleTimeout / 4 &&
                         (keepAlive || _protocolConnection.HasDispatchesInProgress))
                {
                    // We send a ping if there was no activity in the last (IdleTimeout / 4) period. Sending a ping
                    // sooner than really needed is safer to ensure that the receiver will receive the ping in time.
                    // Sending the ping if there was no activity in the last (IdleTimeout / 2) period isn't enough since
                    // Monitor is called only every (IdleTimeout / 2) period. We also send a ping if dispatch are in
                    // progress to notify the peer that we're still alive.
                    //
                    // Note that this doesn't imply that we are sending 4 heartbeats per timeout period because Monitor
                    // is still only called every (IdleTimeout / 2) period.
                    _ = _protocolConnection.PingAsync(CancellationToken.None);
                }
            }
        }

        /// <summary>Closes the connection. Resources allocated for the connection are freed.</summary>
        private void Close(Exception? exception, bool isResumable, IProtocolConnection? protocolConnection = null)
        {
            lock (_mutex)
            {
                if (_state == ConnectionState.NotConnected ||
                    _state == ConnectionState.Closed ||
                    (protocolConnection != null && _protocolConnection != protocolConnection))
                {
                    return;
                }

                // Cancel ConnectAsync if it's pending.
                _connectCancellationSource.Cancel();

                if (_protocolConnection != null)
                {
                    if (exception != null)
                    {
                        _protocolConnection.Abort(exception);
                    }

                    _protocolConnection.Dispose();
                    _protocolConnection = null;
                }
u
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }

                // A connection can be resumed if it's configured to be resumable and the operation that closed the
                // connection allows it (explicit shutdown or abort don't allow the connection to be resumed).
                _state = _options.IsResumable && isResumable ? ConnectionState.NotConnected : ConnectionState.Closed;
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
                        isResumable: true,
                        _shutdownCancellationSource.Token);
                }
            }
        }

        private async Task ShutdownAsync(string message, bool isResumable, CancellationToken cancel)
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
                Close(new ConnectionClosedException(), isResumable);
            }
        }

        private async Task ShutdownAsyncCore(
            IProtocolConnection protocolConnection,
            string message,
            bool isResumable,
            CancellationToken cancel)
        {
            // Yield before continuing to ensure the code below isn't executed with the mutex locked and that _stateTask
            // is assigned before any synchronous continuations are ran.
            await Task.Yield();

            using var closeTimeoutTimer = new Timer(
                value => Close(new ConnectionAbortedException("shutdown timed out"), isResumable, protocolConnection),
                state: null,
                dueTime: _options.CloseTimeout,
                period: Timeout.InfiniteTimeSpan);

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
                Close(exception, isResumable);
            }
        }
    }
}
