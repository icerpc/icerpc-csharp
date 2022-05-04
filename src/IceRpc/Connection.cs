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
        /// <summary>The connection is being gracefully shutdown and waits for the peer to close its end of the
        /// connection before to switch to the <c>Closing</c> state. The peer closes its end of the connection only once
        /// its dispatch complete.</summary>
        ShuttingDown,
        /// <summary>The connection is being closed.</summary>
        Closing,
        /// <summary>The connection is closed and it can't be resumed.</summary>
        Closed
    }

    /// <summary>Represents a connection used to send and receive requests and responses.</summary>
    public sealed class Connection : IAsyncDisposable
    {
        /// <summary>The network connection information or <c>null</c> if the connection is not connected.</summary>
        public NetworkConnectionInformation? NetworkConnectionInformation { get; private set; }

        /// <summary>The connection's endpoint. For a client connection this is the connection's remote endpoint,
        /// for a server connection it's the server's endpoint.</summary>
        public Endpoint Endpoint => _serverEndpoint ?? _options.RemoteEndpoint ?? default;

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

        /// <summary>Gets the features of this connection. These features are empty until the connection is connected.
        /// </summary>
        public FeatureCollection Features { get; private set; } = FeatureCollection.Empty;

        // True once DisposeAsync is called. Once disposed the connection can't be resumed.
        private bool _disposed;

        // The mutex protects mutable data members and ensures the logic for some operations is performed atomically.
        private readonly object _mutex = new();

        private Action<Connection, Exception>? _onClose;

        private readonly ConnectionOptions _options;

        private IProtocolConnection? _protocolConnection;

        private CancellationTokenSource? _protocolShutdownCancellationSource;

        private ConnectionState _state = ConnectionState.NotConnected;

        private readonly Endpoint? _serverEndpoint;

        // The state task is assigned when the state is updated to Connecting, ShuttingDown, Closing. It's completed
        // once the state update completes. It's protected with _mutex.
        private Task? _stateTask;

        private Timer? _timer;

        /// <summary>Constructs a client connection.</summary>
        /// <param name="options">The connection options.</param>
        public Connection(ConnectionOptions options)
        {
            if (options.RemoteEndpoint is not Endpoint remoteEndpoint || remoteEndpoint == default)
            {
                throw new InvalidOperationException(
                    $"cannot create connection without configuring {nameof(ConnectionOptions.RemoteEndpoint)}");
            }
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

        /// <summary>Closes the connection. This methods switches the connection state to <see
        /// cref="ConnectionState.Closing"/>. Once the returned task is completed, the connection will be in the <see
        /// cref="ConnectionState.NotConnected"/> state if <see cref="ConnectionOptions.IsResumable"/> is <c>true</c>,
        /// otherwise it will be <see cref="ConnectionState.Closed"/>.</summary>
        /// <param name="message">A description of the connection close reason.</param>
        public Task CloseAsync(string? message = null) =>
            // TODO: the retry interceptor considers ConnectionClosedException as always retryable. Raising this
            // exception here is therefore wrong when aborting the connection. Invocations which are in progress
            // shouldn't be retried unless not sent or idempotent.
            // TODO2: consider removing this method? Throw ObjectDisposedException instead?
            // TODO3: AbortAsync would be a better name.
            CloseAsync(new ConnectionClosedException(message ?? "connection closed forcefully"));

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
                        Debug.Assert(_options.RemoteEndpoint != null);
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
                    else if (_disposed || _state == ConnectionState.Closed)
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
                    where T : INetworkConnection where TOptions : class
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
        public async ValueTask DisposeAsync()
        {
            Task? waitTask;
            lock (_mutex)
            {
                if (_disposed)
                {
                    waitTask = _stateTask;
                }
                else
                {
                    _disposed = true;

                    // Perform a speedy graceful shutdown by canceling invocations and dispatches in progress.
                    waitTask = ShutdownAsync("connection disposed", new CancellationToken(canceled: true));
                }
            }

            if (waitTask != null)
            {
                try
                {
                    await waitTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.Assert(false, $"dispose exception {ex}");
                }
            }
        }

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
                return _serverEndpoint == null &&
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
                // waiting for AcceptRequestsAsync to throw. It's necessary to ensure that the next InvokeAsync
                // will fail with ConnectionClosedException (it's important to ensure retries don't occur on this
                // connection again).
                await CloseAsync(exception).ConfigureAwait(false);
                throw;
            }
            catch (ConnectionClosedException exception)
            {
                // Ensure that the shutdown is initiated if the invocations fails with ConnectionClosedException. It's
                // possible that the connection didn't receive yet the GoAway message. Initiating the shutdown now
                // ensures that the next InvokeAsync will fail with ConnectionClosedException (it's important to
                // ensure retries don't occur on this connection again).
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
        public async Task ShutdownAsync(string message, CancellationToken cancel = default)
        {
            // TODO: should we keep this IceRPC-protocol only feature to transmit the shutdown message over-the-wire?
            // The message will be accessible to the application through the message of the ConnectionClosedException
            // raised when pending invocation are canceled because of the shutdown. If we keep it we should add a
            // similar method on Server.

            Task shutdownTask;
            CancellationTokenSource? cancellationTokenSource = null;
            lock (_mutex)
            {
                if (_state == ConnectionState.Active)
                {
                    _state = ConnectionState.ShuttingDown;
                    _protocolShutdownCancellationSource = new();
                    _stateTask = ShutdownAsyncCore(
                        _protocolConnection!,
                        message,
                        _protocolShutdownCancellationSource.Token);
                    shutdownTask = _stateTask;
                }
                else if (_state < ConnectionState.Active)
                {
                    shutdownTask = CloseAsync(new ConnectionClosedException(message));
                }
                else
                {
                    shutdownTask = _stateTask ?? CloseAsync(new ConnectionClosedException(message));
                }
                cancellationTokenSource = _protocolShutdownCancellationSource;
            }

            // If the application cancels ShutdownAsync, cancel the protocol ShutdownAsync call.
            using CancellationTokenRegistration _ = cancel.Register(() =>
                {
                    try
                    {
                        cancellationTokenSource?.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                });

            // Wait for the shutdown to complete.
            await shutdownTask.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override string ToString() => Endpoint.ToString();

        /// <summary>Constructs a server connection from an accepted network connection.</summary>
        internal Connection(Endpoint endpoint, ConnectionOptions options)
        {
            _serverEndpoint = endpoint;
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
            Action<Connection, Exception>? onClose) where T : INetworkConnection where TOptions : class
        {
            using var connectTimeoutCancellationSource = new CancellationTokenSource(_options.ConnectTimeout);
            try
            {
                // Make sure we establish the connection asynchronously without holding any mutex lock from the caller.
                await Task.Yield();

                // Establish the network connection.
                NetworkConnectionInformation = await networkConnection.ConnectAsync(
                    connectTimeoutCancellationSource.Token).ConfigureAwait(false);

                var features = new FeatureCollection(_options.Features);

                // Create the protocol connection.
                _protocolConnection = await protocolConnectionFactory.CreateProtocolConnectionAsync(
                    networkConnection,
                    NetworkConnectionInformation.Value,
                    _options.Dispatcher,
                    _options.OnConnect == null ? null : fields => _options.OnConnect(this, fields, features),
                    _serverEndpoint != null,
                    protocolOptions,
                    connectTimeoutCancellationSource.Token).ConfigureAwait(false);

                lock (_mutex)
                {
                    if (_state >= ConnectionState.Closing)
                    {
                        // This can occur if the connection is closed while the connection is being connected.
                        throw new ConnectionClosedException();
                    }

                    _state = ConnectionState.Active;
                    _stateTask = null;
                    Features = features;

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

                    // Start accepting requests. _protocolConnection might be updated before the task is ran so we
                    // capture the protocol connection to ensure we accept requests on this new connection.
                    IProtocolConnection protocolConnection = _protocolConnection;
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

                            lock (_mutex)
                            {
                                // If the connection hasn't been resumed already, close it.
                                if (protocolConnection == _protocolConnection)
                                {
                                    _ = CloseAsync(exception);
                                }
                            }
                        });
                }
            }
            catch (OperationCanceledException)
            {
                var exception = new ConnectTimeoutException();
                await CloseAsync(exception).ConfigureAwait(false);
                throw exception;
            }
            catch (Exception exception)
            {
                await CloseAsync(exception).ConfigureAwait(false);
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
                        // Close the connection if we didn't receive a heartbeat and the connection is idle.
                        // The server is supposed to send heartbeats when dispatch are in progress.
                        _ = CloseAsync("connection timed out");
                    }
                    else
                    {
                        // The connection is idle, close it.
                        _ = ShutdownAsync("connection idle", CancellationToken.None);
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

        /// <summary>Closes the connection. This will forcefully close the connection if the connection hasn't been
        /// shutdown gracefully. Resources allocated for the connection are freed. The connection can be re-established
        /// once this method returns by calling <see cref="ConnectAsync"/>.</summary>
        private async Task CloseAsync(Exception? exception)
        {
            Task waitTask;
            lock (_mutex)
            {
                if (_state == ConnectionState.NotConnected)
                {
                    return;
                }
                else if (_state != ConnectionState.Closing)
                {
                    // It's important to set the state before performing the close. The close of the streams
                    // will trigger the failure of the associated invocations whose interceptor might access
                    // the connection state (e.g.: the retry interceptor or the connection pool checks the
                    // connection state).
                    _state = ConnectionState.Closing;
                    _stateTask = PerformCloseAsync();
                }

                Debug.Assert(_stateTask != null);
                waitTask = _stateTask;
            }

            await waitTask.ConfigureAwait(false);

            async Task PerformCloseAsync()
            {
                // Yield before continuing to ensure the code below isn't executed with the mutex locked and
                // that _closeTask is assigned before any synchronous continuations are ran.
                await Task.Yield();

                if (_protocolConnection is IProtocolConnection protocolConnection)
                {
                    await protocolConnection.DisposeAsync().ConfigureAwait(false);
                }

                if (_timer != null)
                {
                    await _timer.DisposeAsync().ConfigureAwait(false);
                }

                _protocolShutdownCancellationSource?.Dispose();

                lock (_mutex)
                {
                    // A connection can be resumed if it hasn't been disposed and it's configured to be resumable.
                    _state = (_options?.IsResumable ?? false) && !_disposed ?
                        ConnectionState.NotConnected : ConnectionState.Closed;

                    _stateTask = null;
                    _protocolConnection = null;
                    _timer = null;
                    _protocolShutdownCancellationSource = null;
                }

                // Raise the Closed event, this will call user code so we shouldn't hold the mutex.
                if (State == ConnectionState.Closed)
                {
                    try
                    {
                        // TODO: pass a null exception instead? See issue #1100.
                        (_onClose + _options?.OnClose)?.Invoke(
                            this,
                            exception ?? new ConnectionClosedException("connection gracefully shut down"));
                    }
                    catch
                    {
                        // Ignore, on close actions shouldn't raise exceptions.
                    }
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
                    _protocolShutdownCancellationSource = new();
                    _stateTask = ShutdownAsyncCore(
                        _protocolConnection!,
                        message,
                        _protocolShutdownCancellationSource.Token);
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

            // If shutdown is canceled, cancel the pending invocations and dispatches from the protocol connection to
            // speed up shutdown.
            cancel.Register(protocolConnection.CancelPendingInvocationsAndDispatchesOnShutdown);

            using var closeTimeoutCancellationSource = new CancellationTokenSource(_options.CloseTimeout);
            Exception exception;
            try
            {
                // Shutdown the connection. If the given cancellation token is canceled, pending invocations and
                // dispatches are canceled to speed up shutdown. Otherwise, the protocol shutdown is canceled on close
                // timeout.
                await protocolConnection.ShutdownAsync(
                    message,
                    closeTimeoutCancellationSource.Token).ConfigureAwait(false);

                // Close the connection.
                exception = new ConnectionClosedException(message);
            }
            catch (OperationCanceledException)
            {
                exception = new ConnectionClosedException("shutdown timed out");
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            Task? closeTask = null;
            lock (_mutex)
            {
                if (_protocolConnection == protocolConnection)
                {
                    closeTask = CloseAsync(exception);
                }
                else
                {
                    // The connection has already been closed so there's no need to close it again. This can occur
                    // if the protocol connection raise from ReceiveRequestAsync.
                }
            }

            if (closeTask != null)
            {
                await closeTask.ConfigureAwait(false);
            }
        }
    }
}
