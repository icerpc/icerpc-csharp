// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO.Pipelines;
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

    /// <summary>Event arguments for the <see cref="Connection.Closed"/> event.</summary>
    public sealed class ClosedEventArgs : EventArgs
    {
        /// <summary>The exception responsible for the connection closure.</summary>
        public Exception Exception { get; }

        internal ClosedEventArgs(Exception exception) => Exception = exception;
    }

    /// <summary>Represents a connection used to send and receive requests and responses.</summary>
    public sealed class Connection : IAsyncDisposable
    {
        /// <summary>This event is raised when the connection is closed. The connection object is passed as the
        /// event sender argument. The event handler should not throw.</summary>
        /// <exception cref="InvalidOperationException">Thrown on event addition if the connection is closed.
        /// </exception>
        public event EventHandler<ClosedEventArgs>? Closed
        {
            add
            {
                lock (_mutex)
                {
                    if (_state == ConnectionState.Closed)
                    {
                        throw new InvalidOperationException("the connection is closed");
                    }
                    _closed += value;
                }
            }
            remove => _closed -= value;
        }

        /// <summary><c>true</c> for a connection accepted by a server and <c>false</c> for a connection created by a
        /// client.</summary>
        public bool IsServer => _options == null;

        /// <summary>The network connection information or <c>null</c> if the connection is not connected.</summary>
        public NetworkConnectionInformation? NetworkConnectionInformation { get; private set; }

        /// <summary>The protocol used by the connection.</summary>
        public Protocol Protocol { get; }

        /// <summary>The connection's remote endpoint.</summary>
        public Endpoint RemoteEndpoint => NetworkConnectionInformation?.RemoteEndpoint ??
            _options?.RemoteEndpoint ??
            throw new InvalidOperationException($"{nameof(RemoteEndpoint)} is not set on the connection");

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

        private EventHandler<ClosedEventArgs>? _closed;

        private readonly TimeSpan _closeTimeout;

        // True once DisposeAsync is called. Once disposed the connection can't be resumed.
        private bool _disposed;

        // The mutex protects mutable data members and ensures the logic for some operations is performed atomically.
        private readonly object _mutex = new();

        private INetworkConnection? _networkConnection;

        // _options is null for server connections and non-null for client connections.
        private readonly ConnectionOptions? _options;

        private IProtocolConnection? _protocolConnection;

        private CancellationTokenSource? _protocolShutdownCancellationSource;

        private ConnectionState _state = ConnectionState.NotConnected;

        // The state task is assigned when the state is updated to Connecting, ShuttingDown, Closing. It's completed
        // once the state update completes. It's protected with _mutex.
        private Task? _stateTask;

        private Timer? _timer;

        /// <summary>Constructs a client connection.</summary>
        /// <param name="options">The connection options.</param>
        public Connection(ConnectionOptions options)
        {
            Protocol = options.RemoteEndpoint is Endpoint remoteEndpoint ? remoteEndpoint.Protocol :
                throw new ArgumentException($"options.RemoteEndpoint must be set to a non-null value", nameof(options));

            _closeTimeout = options.CloseTimeout;
            _options = options;
        }

        /// <summary>Constructs a client connection with the specified remote endpoint and  authentication options.
        /// All other properties have their default values.</summary>
        /// <param name="remoteEndpoint">The remote endpoint.</param>
        /// <param name="authenticationOptions">The client authentication options.</param>
        public Connection(Endpoint remoteEndpoint, SslClientAuthenticationOptions? authenticationOptions = null)
            : this(new ConnectionOptions
            {
                AuthenticationOptions = authenticationOptions,
                RemoteEndpoint = remoteEndpoint
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
                        // Only the application can call ConnectAsync on a server connection (which is ok but not
                        // particularly useful), and in this case, the connection state can only be active or >=
                        // closing.
                        Debug.Assert(_options != null);

                        Debug.Assert(
                            _networkConnection == null &&
                            _protocolConnection == null &&
                            RemoteEndpoint != default);

                        _stateTask = Protocol == Protocol.Ice ?
                            PerformConnectAsync(
                                _options.SimpleClientTransport,
                                IceProtocol.Instance.ProtocolConnectionFactory,
                                LogSimpleNetworkConnectionDecorator.Decorate) :
                            PerformConnectAsync(
                                _options.MultiplexedClientTransport,
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

            Task PerformConnectAsync<T>(
                IClientTransport<T> clientTransport,
                IProtocolConnectionFactory<T> protocolConnectionFactory,
                LogNetworkConnectionDecoratorFactory<T> logDecoratorFactory) where T : INetworkConnection
            {
                // This is the composition root of client Connections, where we install log decorators when logging is
                // enabled.

                ILogger logger = _options.LoggerFactory.CreateLogger("IceRpc.Client");

                T networkConnection = clientTransport.CreateConnection(
                    RemoteEndpoint,
                    _options.AuthenticationOptions,
                    logger);

                EventHandler<ClosedEventArgs>? closedEventHandler = null;

                if (logger.IsEnabled(LogLevel.Error)) // TODO: log level
                {
                    networkConnection = logDecoratorFactory(networkConnection, RemoteEndpoint, isServer: false, logger);

                    protocolConnectionFactory =
                        new LogProtocolConnectionFactoryDecorator<T>(protocolConnectionFactory, logger);

                    closedEventHandler = (sender, args) =>
                    {
                        if (args.Exception is Exception exception)
                        {
                            // This event handler is added/executed after NetworkConnectionInformation is set.
                            using IDisposable scope =
                                logger.StartClientConnectionScope(NetworkConnectionInformation!.Value);
                            logger.LogConnectionClosedReason(exception);
                        }
                    };
                }

                // This local function is called with _mutex locked and executes synchronously until the call to
                // ConnectAsync so it's safe to assign _networkConnection here.
                _networkConnection = networkConnection;
                _state = ConnectionState.Connecting;

                return ConnectAsync(
                    networkConnection,
                    _options.Dispatcher,
                    protocolConnectionFactory,
                    _options.ConnectTimeout,
                    _options.IncomingFrameMaxSize,
                    _options.KeepAlive,
                    closedEventHandler);
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
                return IsServer == false &&
                       State == ConnectionState.Active &&
                       _networkConnection!.HasCompatibleParams(remoteEndpoint);
            }
        }

        /// <inheritdoc/>
        public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // A connection can be closed concurrently so as long as ConnectAsync succeeds, we loop to get the
            // protocol connection.
            IProtocolConnection protocolConnection;
            while (true)
            {
                lock (_mutex)
                {
                    if (_state == ConnectionState.Active)
                    {
                        protocolConnection = _protocolConnection!;
                        break; // while
                    }
                }

                await ConnectAsync(cancel).ConfigureAwait(false);
            }

            // Send the request. This completes payload source; this also completes payload sink when the Send fails
            // with an exception or there is no payload source stream.
            await protocolConnection.SendRequestAsync(request, cancel).ConfigureAwait(false);

            // Wait for the response if it's a two-way request, otherwise return a response with an empty payload.
            IncomingResponse response = request.IsOneway ?
                new IncomingResponse(request, ResultType.Success, EmptyPipeReader.Instance) :
                await protocolConnection.ReceiveResponseAsync(request, cancel).ConfigureAwait(false);

            response.Connection = this;
            return response;
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
                }
                shutdownTask = _stateTask ?? CloseAsync(new ConnectionClosedException(message));
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
        public override string ToString() => _networkConnection?.ToString() ?? "";

        /// <summary>Constructs a server connection from an accepted network connection.</summary>
        internal Connection(INetworkConnection connection, Protocol protocol, TimeSpan closeTimeout)
        {
            Protocol = protocol;
            _networkConnection = connection;
            _closeTimeout = closeTimeout;
            _state = ConnectionState.Connecting;
        }

        /// <summary>Establishes a connection. This method is used for both client and server connections.</summary>
        /// <param name="networkConnection">The underlying network connection.</param>
        /// <param name="dispatcher">The dispatcher.</param>
        /// <param name="protocolConnectionFactory">The protocol connection factory.</param>
        /// <param name="connectTimeout">The connect timeout.</param>
        /// <param name="incomingFrameMaxSize">The incoming frame max size.</param>
        /// <param name="keepAlive">Whether or not to keep the new connection alive.</param>
        /// <param name="closedEventHandler">A closed event handler added to the connection once the connection is
        /// active.</param>
        internal async Task ConnectAsync<T>(
            T networkConnection,
            IDispatcher dispatcher,
            IProtocolConnectionFactory<T> protocolConnectionFactory,
            TimeSpan connectTimeout,
            int incomingFrameMaxSize,
            bool keepAlive,
            EventHandler<ClosedEventArgs>? closedEventHandler) where T : INetworkConnection
        {
            using var connectCancellationSource = new CancellationTokenSource(connectTimeout);
            try
            {
                // Make sure we establish the connection asynchronously without holding any mutex lock from the caller.
                await Task.Yield();

                // Establish the network connection.
                NetworkConnectionInformation = await networkConnection.ConnectAsync(
                    connectCancellationSource.Token).ConfigureAwait(false);

                // Create the protocol connection.
                _protocolConnection = await protocolConnectionFactory.CreateProtocolConnectionAsync(
                    networkConnection,
                    NetworkConnectionInformation.Value,
                    incomingFrameMaxSize,
                    IsServer,
                    connectCancellationSource.Token).ConfigureAwait(false);

                lock (_mutex)
                {
                    if (_state >= ConnectionState.Closing)
                    {
                        // This can occur if the connection is closed while the connection is being connected.
                        throw new ConnectionClosedException();
                    }

                    _state = ConnectionState.Active;
                    _stateTask = null;

                    _closed += closedEventHandler;

                    // Switch the connection to the ShuttingDown state as soon as the protocol receives a notification
                    // that peer initiated shutdown. This is in particular useful for the connection pool to not return
                    // a connection which is being shutdown.
                    _protocolConnection.PeerShutdownInitiated += message =>
                        {
                            lock (_mutex)
                            {
                                if (_state == ConnectionState.Active)
                                {
                                    _state = ConnectionState.ShuttingDown;
                                    _protocolShutdownCancellationSource = new();
                                    _stateTask = ShutdownAsyncCore(
                                        _protocolConnection,
                                        message,
                                        _protocolShutdownCancellationSource.Token);
                                }
                            }
                        };

                    // Setup a timer to check for the connection idle time every IdleTimeout / 2 period. If the
                    // transport doesn't support idle timeout (e.g.: the colocated transport), IdleTimeout will be
                    // infinite.
                    TimeSpan idleTimeout = NetworkConnectionInformation!.Value.IdleTimeout;
                    if (idleTimeout != TimeSpan.MaxValue && idleTimeout != Timeout.InfiniteTimeSpan)
                    {
                        _timer = new Timer(value => Monitor(keepAlive), null, idleTimeout / 2, idleTimeout / 2);
                    }

                    // Start the receive request task. The task accepts new incoming requests and processes them. It
                    // only completes once the connection is closed.
                    _ = Task.Run(
                        () => AcceptIncomingRequestAsync(_protocolConnection, dispatcher),
                        CancellationToken.None);
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
                Debug.Assert(
                    _networkConnection != null &&
                    _protocolConnection != null &&
                    NetworkConnectionInformation != null);

                TimeSpan idleTime = Time.Elapsed - _networkConnection!.LastActivity;
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
                    // We send a ping if there was no activity in the last (IdleTimeout / 4) period. Sending a
                    // ping sooner than really needed is safer to ensure that the receiver will receive the
                    // ping in time. Sending the ping if there was no activity in the last (IdleTimeout / 2)
                    // period isn't enough since Monitor is called only every (IdleTimeout / 2) period. We
                    // also send a ping if dispatch are in progress to notify the peer that we're still alive.
                    //
                    // Note that this doesn't imply that we are sending 4 heartbeats per timeout period
                    // because Monitor is still only called every (IdleTimeout / 2) period.
                    _ = _protocolConnection.PingAsync(CancellationToken.None);
                }
            }
        }

        /// <summary>Accepts an incoming request and dispatch it. As soon as new incoming request is accepted
        /// but before it's dispatched, a new accept incoming request task is started to allow multiple
        /// incoming requests to be dispatched. The protocol implementation can limit the number of concurrent
        /// dispatch by no longer accepting a new request when a limit is reached.</summary>
        private async Task AcceptIncomingRequestAsync(IProtocolConnection protocolConnection, IDispatcher dispatcher)
        {
            IncomingRequest request;
            try
            {
                request = await protocolConnection.ReceiveRequestAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Unexpected exception, if the connection hasn't been resumed already, close the connection.
                lock (_mutex)
                {
                    if (protocolConnection == _protocolConnection)
                    {
                        _ = CloseAsync(exception);
                    }
                }
                return;
            }

            // Start a new task to accept a new incoming request before dispatching this one.
            _ = Task.Run(() => AcceptIncomingRequestAsync(protocolConnection, dispatcher));

            OutgoingResponse? response = null;

            try
            {
                // Dispatch the request and get the response.
                try
                {
                    CancellationToken cancel = request.CancelDispatchSource?.Token ?? default;
                    request.Connection = this;
                    response = await dispatcher.DispatchAsync(request, cancel).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // If we catch an exception, we return a failure response with a Slice-encoded payload.

                    if (exception is OperationCanceledException)
                    {
                        // TODO: do we really need this protocol-dependent processing?
                        if (Protocol == Protocol.Ice)
                        {
                            exception = new DispatchException("dispatch canceled by peer", DispatchErrorCode.Canceled);
                        }
                        else
                        {
                            // Rethrow to abort the stream.
                            throw;
                        }
                    }

                    // With the ice protocol, a ResultType = Failure exception must be an ice system exception.
                    if (exception is not RemoteException remoteException ||
                        remoteException.ConvertToUnhandled ||
                        (Protocol == Protocol.Ice && remoteException is not DispatchException))
                    {
                        remoteException = new DispatchException(
                            message: null,
                            exception is InvalidDataException ?
                                DispatchErrorCode.InvalidData : DispatchErrorCode.UnhandledException,
                            exception);
                    }

                    SliceEncoding sliceEncoding = request.Protocol.SliceEncoding!;

                    response = new OutgoingResponse(request)
                    {
                        PayloadSource = sliceEncoding.CreatePayloadFromRemoteException(remoteException),
                        ResultType = ResultType.Failure
                    };

                    if (Protocol.HasFields && remoteException.RetryPolicy != RetryPolicy.NoRetry)
                    {
                        RetryPolicy retryPolicy = remoteException.RetryPolicy;
                        response.Fields = response.Fields.With(
                            ResponseFieldKey.RetryPolicy,
                            (ref SliceEncoder encoder) => retryPolicy.Encode(ref encoder));
                    }
                }

                await protocolConnection.SendResponseAsync(
                    response,
                    request,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // TODO: we shouldn't have this protocol specific handling of OperationCanceledException here.
                Exception exception = IceRpcStreamError.DispatchCanceled.ToException();

                await request.CompleteAsync(exception).ConfigureAwait(false);
                if (response == null)
                {
                    await request.ResponseWriter.CompleteAsync(exception).ConfigureAwait(false);
                }
                else
                {
                    await response.CompleteAsync(exception).ConfigureAwait(false);
                }
            }
            catch (MultiplexedStreamAbortedException)
            {
                // Ignore, the peer aborted the stream and at this point, there's not much we can do to report it.
            }
            catch (Exception exception)
            {
                lock (_mutex)
                {
                    // Unexpected exception, if the connection hasn't been resumed already, close the connection.
                    if (protocolConnection == _protocolConnection)
                    {
                        _ = CloseAsync(exception);
                    }
                }
            }
        }

        /// <summary>Closes the connection. This will forcefully close the connection if the connection hasn't been
        /// shutdown gracefully. Resources allocated for the connection are freed. The connection can be re-established
        /// once this method returns by calling <see cref="ConnectAsync"/>.</summary>
        private async Task CloseAsync(Exception exception)
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

                try
                {
                    _protocolConnection?.Dispose();
                }
                catch (Exception ex)
                {
                    // The protocol or transport aren't supposed to raise.
                    Debug.Assert(false, $"unexpected protocol close exception\n{ex}");
                }

                if (_networkConnection is INetworkConnection networkConnection)
                {
                    try
                    {
                        await networkConnection.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // The protocol or transport aren't supposed to raise.
                        Debug.Assert(false, $"unexpected transport close exception\n{ex}");
                    }
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
                    _networkConnection = null;
                    _timer = null;
                    _protocolShutdownCancellationSource = null;
                }

                // Raise the Closed event, this will call user code so we shouldn't hold the mutex.
                if (State == ConnectionState.Closed)
                {
                    try
                    {
                        _closed?.Invoke(this, new ClosedEventArgs(exception));
                    }
                    catch
                    {
                        // Ignore, application event handlers shouldn't raise exceptions.
                    }
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

            using var closeCancellationSource = new CancellationTokenSource(_closeTimeout);
            try
            {
                // Shutdown the connection.
                await protocolConnection
                    .ShutdownAsync(message, cancel)
                    .WaitAsync(closeCancellationSource.Token)
                    .ConfigureAwait(false);

                // Close the connection.
                await CloseAsync(new ConnectionClosedException(message)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await CloseAsync(new ConnectionClosedException("shutdown timed out")).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await CloseAsync(exception).ConfigureAwait(false);
            }
        }
    }
}
