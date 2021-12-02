// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>The state of an IceRpc connection.</summary>
    public enum ConnectionState : byte
    {
        /// <summary>The connection is not connected.</summary>
        NotConnected,
        /// <summary>The connection establishment is in progress.</summary>
        Connecting,
        /// <summary>The connection is active and can send and receive messages.</summary>
        Active,
        /// <summary>The connection is being gracefully shutdown and waits for the peer to close its end of the
        /// connection before to switch to the <c>Closed</c> state. The peer closes its end of the connection only once
        /// its dispatch complete.</summary>
        Closing,
        /// <summary>The connection is closed.</summary>
        Closed
    }

    /// <summary>Event arguments for the <see cref="Connection.Closed"/> event.</summary>
    public sealed class ClosedEventArgs : EventArgs
    {
        /// <summary>The exception responsible for the connection closure.</summary>
        public Exception Exception { get; }

        internal ClosedEventArgs(Exception exception) => Exception = exception;
    }

    /// <summary>Represents a connection used to send and receive Ice frames.</summary>
    public sealed class Connection : IAsyncDisposable
    {
        /// <summary>The default value for <see cref="MultiplexedClientTransport"/>.</summary>
        public static IClientTransport<IMultiplexedNetworkConnection> DefaultMultiplexedClientTransport { get; } =
            new CompositeMultiplexedClientTransport().UseSlicOverColoc().UseSlicOverTcp();

        /// <summary>The default value for <see cref="SimpleClientTransport"/>.</summary>
        public static IClientTransport<ISimpleNetworkConnection> DefaultSimpleClientTransport { get; } =
            new CompositeSimpleClientTransport().UseColoc().UseTcp().UseUdp();

        /// <summary>This event is raised when the connection is closed. The connection object is passed as the
        /// event sender argument. The event handler should not throw.</summary>
        /// <exception cref="InvalidOperationException">Thrown on event addition if the connection is closed.
        /// </exception>
        public event EventHandler<ClosedEventArgs>? Closed
        {
            add
            {
                if (_state >= ConnectionState.Closed)
                {
                    throw new InvalidOperationException("the connection is closed");
                }
                _closed += value;
            }
            remove => _closed -= value;
        }

        /// <summary>Gets or sets the dispatcher that dispatches requests received by this
        /// connection.</summary>
        /// <value>The dispatcher that dispatches requests received by this connection, or null if no
        /// dispatcher is set.</value>
        public IDispatcher? Dispatcher { get; init; }

        /// <summary>The logger factory used to create loggers to log connection-related activities.</summary>
        public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

        /// <summary>The <see cref="IClientTransport{IMultiplexedNetworkConnection}"/> used by this connection to
        /// create multiplexed network connections.</summary>
        public IClientTransport<IMultiplexedNetworkConnection> MultiplexedClientTransport { get; init; } =
            DefaultMultiplexedClientTransport;

        /// <summary>The <see cref="IClientTransport{ISimpleNetworkConnection}"/> used by this connection to create
        /// simple network connections.</summary>
        public IClientTransport<ISimpleNetworkConnection> SimpleClientTransport { get; init; } =
            DefaultSimpleClientTransport;

        /// <summary><c>true</c> if the connection uses a secure transport, <c>false</c> otherwise.</summary>
        /// <remarks><c>false</c> can mean the connection is not yet connected and its security will be determined
        /// during connection establishment.</remarks>
        public bool IsSecure => _networkConnection?.IsSecure ?? false;

        /// <summary><c>true</c> for a connection accepted by a server and <c>false</c> for a connection created by a
        /// client.</summary>
        public bool IsServer => _protocol != null;

        /// <summary>The network connection information or <c>null</c> if the connection is not connected.</summary>
        public NetworkConnectionInformation? NetworkConnectionInformation { get; private set; }

        /// <summary>The protocol used by the connection.</summary>
        public Protocol Protocol => _protocol ?? RemoteEndpoint.Protocol;

        /// <summary>Gets or sets the options of the connection.</summary>
        public ConnectionOptions Options { get; init; } = new();

        /// <summary>The connection's remote endpoint.</summary>
        public Endpoint RemoteEndpoint
        {
            get => NetworkConnectionInformation?.RemoteEndpoint ??
                   _initialRemoteEndpoint ??
                   throw new InvalidOperationException($"{nameof(RemoteEndpoint)} is not set on the connection");
            init => _initialRemoteEndpoint = value;
        }

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

        // The connect task is assigned when ConnectAsync is called, it's protected with _mutex.
        private Task? _connectTask;

        private EventHandler<ClosedEventArgs>? _closed;

        // The close task is assigned when ShutdownAsync or CloseAsync are called, it's protected with _mutex.
        private Task? _closeTask;

        // The initial remote endpoint for client connections.
        private readonly Endpoint? _initialRemoteEndpoint;

        // The mutex protects mutable data members and ensures the logic for some operations is performed atomically.
        private readonly object _mutex = new();

        private INetworkConnection? _networkConnection;

        // _protocol is non-null only for server connections. For client connections, it's null. The protocol
        // is instead obtained with RemoteEndpoint.Protocol
        private readonly Protocol? _protocol;

        private IProtocolConnection? _protocolConnection;

#pragma warning disable CA2213 // _protocolShutdownCancellationSource is disposed in CloseAsync
        private readonly CancellationTokenSource _protocolShutdownCancellationSource = new();
#pragma warning restore CA2213

        private ConnectionState _state = ConnectionState.NotConnected;

#pragma warning disable CA2213 // _timer is disposed in CloseAsync
        private Timer? _timer;
#pragma warning restore CA2213

        /// <summary>Constructs a new client connection.</summary>
        public Connection()
        {
        }

        /// <summary>Closes the connection. This methods switches the connection state to <see
        /// cref="ConnectionState.Closed"/>. If <see cref="Closed"/> event listeners are registered, it waits
        /// for the events to be executed.</summary>
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
        /// <exception cref="ObjectDisposedException">Thrown if the connection is already closed.</exception>
        public Task ConnectAsync(CancellationToken cancel = default)
        {
            lock (_mutex)
            {
                if (_state == ConnectionState.Active)
                {
                    return Task.CompletedTask;
                }
                else if (_state >= ConnectionState.Closing)
                {
                    // TODO: resume the connection if it's resumable
                    throw new ConnectionClosedException();
                }

                // Only the application can call ConnectAsync on a server connection (which is ok but not particularly
                // useful), and in this case, the connection state can only be active or >= closing.
                Debug.Assert(!IsServer);

                if (_state == ConnectionState.NotConnected)
                {
                    Debug.Assert(!IsServer);
                    Debug.Assert(_protocolConnection == null && RemoteEndpoint != null);

                    _connectTask = Protocol == Protocol.Ice1 ?
                        PerformConnectAsync(SimpleClientTransport,
                                            Ice1Protocol.Instance.ProtocolConnectionFactory,
                                            LogSimpleNetworkConnectionDecorator.Decorate) :
                        PerformConnectAsync(MultiplexedClientTransport,
                                            Ice2Protocol.Instance.ProtocolConnectionFactory,
                                            LogMultiplexedNetworkConnectionDecorator.Decorate);
                }

                Debug.Assert(_state == ConnectionState.Connecting);
                Debug.Assert(_connectTask != null);
            }

            return _connectTask.WaitAsync(cancel);

            Task PerformConnectAsync<T>(
                IClientTransport<T> clientTransport,
                IProtocolConnectionFactory<T> protocolConnectionFactory,
                LogNetworkConnectionDecoratorFactory<T> logDecoratorFactory) where T : INetworkConnection
            {
                // This is the composition root of client Connections, where we install log decorators when logging is
                // enabled.

                ILogger logger = LoggerFactory.CreateLogger("IceRpc.Client");

                T networkConnection = clientTransport.CreateConnection(RemoteEndpoint, logger);

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

                return ConnectAsync(networkConnection, protocolConnectionFactory, closedEventHandler);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await ShutdownAsync("connection disposed", new CancellationToken(canceled: true)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.Assert(false, $"dispose exception {ex}");
            }
        }

        /// <summary>Checks if the parameters of the provided endpoint are compatible with this connection. Compatible
        /// means a client could reuse this connection instead of establishing a new connection.</summary>
        /// <param name="remoteEndpoint">The endpoint to check.</param>
        /// <returns><c>true</c> when this connection is an active client connection whose parameters are compatible
        /// with the parameters of the provided endpoint; otherwise, <c>false</c>.</returns>
        /// <remarks>This method checks only the parameters of the endpoint; it does not check other properties.
        /// </remarks>
        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            IsServer == false &&
            State == ConnectionState.Active &&
            _networkConnection!.HasCompatibleParams(remoteEndpoint);

        /// <inheritdoc/>
        public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // Make sure the connection is connected.
            await ConnectAsync(cancel).ConfigureAwait(false);

            try
            {
                // Send the request.
                await _protocolConnection!.SendRequestAsync(request, cancel).ConfigureAwait(false);

                // Wait for the response if two-way request, otherwise return a response with an empty payload.
                IncomingResponse response;
                if (request.IsOneway)
                {
                    response = new IncomingResponse(Protocol, ResultType.Success)
                    {
                        PayloadEncoding = request.PayloadEncoding,
                        Payload = default
                    };
                }
                else
                {
                    response = await _protocolConnection.ReceiveResponseAsync(request, cancel).ConfigureAwait(false);
                }
                response.Connection = this;
                return response;
            }
            catch (OperationCanceledException)
            {
                request.Stream?.Abort(MultiplexedStreamError.InvocationCanceled);
                throw;
            }
        }

        /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
        /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public Task ShutdownAsync(CancellationToken cancel = default) => ShutdownAsync("connection shutdown", cancel);

        /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
        /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
        /// <param name="message">The message transmitted to the peer (when using the Ice2 protocol).</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public async Task ShutdownAsync(string message, CancellationToken cancel = default)
        {
            // TODO: should we keep this Ice2-only feature to transmit the shutdown message over-the-wire? The message
            // will be accessible to the application through the message of the ConnectionClosedException raised when
            // pending invocation are canceled because of the shutdown. If we keep it we should add a similar method on
            // Server.

            Task shutdownTask;
            lock (_mutex)
            {
                // The connection might already be in the closing state if peer initiated shutdown. We still perform
                // shutdown in this case to wait for shutdown completion.
                if (_state == ConnectionState.Active || _state == ConnectionState.Closing)
                {
                    _state = ConnectionState.Closing;
                    _closeTask ??= PerformShutdownAsync();
                }
                shutdownTask = _closeTask ?? CloseAsync(new ConnectionClosedException(message));
            }

            // If the application cancels ShutdownAsync, cancel the protocol ShutdownAsync call.
            using CancellationTokenRegistration _ = cancel.Register(() =>
                {
                    try
                    {
                        _protocolShutdownCancellationSource.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                });

            // Wait for the shutdown to complete.
            await shutdownTask.ConfigureAwait(false);

            async Task PerformShutdownAsync()
            {
                // Yield before continuing to ensure the code below isn't executed with the mutex locked and
                // that _closeTask is assigned before any synchronous continuations are ran.
                await Task.Yield();

                using var closeCancellationSource = new CancellationTokenSource(Options.CloseTimeout);
                try
                {
                    // Shutdown the connection. The _shutdownCancellationSource is used to speed up the shutdown
                    // if the application cancels ShutdownAsync.
                    await _protocolConnection!
                        .ShutdownAsync(message, _protocolShutdownCancellationSource.Token)
                        .WaitAsync(closeCancellationSource.Token).ConfigureAwait(false);

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

        /// <inheritdoc/>
        public override string ToString() => _networkConnection?.ToString() ?? "";

        /// <summary>Constructs a server connection from an accepted network connection.</summary>
        internal Connection(INetworkConnection connection, Protocol protocol)
        {
            _networkConnection = connection;
            _protocol = protocol;
            _state = ConnectionState.Connecting;
        }

        /// <summary>Establishes a connection. This method is used for both client and server connections.</summary>
        /// <param name="networkConnection">The underlying network connection.</param>
        /// <param name="protocolConnectionFactory">The protocol connection factory.</param>
        /// <param name="closedEventHandler">A closed event handler added to the connection once the connection is
        /// active.</param>
        internal async Task ConnectAsync<T>(
            T networkConnection,
            IProtocolConnectionFactory<T> protocolConnectionFactory,
            EventHandler<ClosedEventArgs>? closedEventHandler) where T : INetworkConnection
        {
            using var connectCancellationSource = new CancellationTokenSource(Options.ConnectTimeout);
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
                    Options.IncomingFrameMaxSize,
                    IsServer,
                    connectCancellationSource.Token).ConfigureAwait(false);

                lock (_mutex)
                {
                    if (_state == ConnectionState.Closed)
                    {
                        // This can occur if the connection is disposed while the connection is being initialized.
                        throw new ConnectionClosedException();
                    }

                    _state = ConnectionState.Active;

                    _closed += closedEventHandler;

                    // Switch the connection to the Closing state as soon as the protocol receives a notification that
                    // peer initiated shutdown. This is in particular useful for the connection pool to not return a
                    // connection which is being shutdown.
                    _protocolConnection.PeerShutdownInitiated += () =>
                        {
                            lock (_mutex)
                            {
                                if (_state == ConnectionState.Active)
                                {
                                    _state = ConnectionState.Closing;
                                }
                            }
                        };

                    // Setup a timer to check for the connection idle time every IdleTimeout / 2 period. If the
                    // transport doesn't support idle timeout (e.g.: the colocated transport), IdleTimeout will
                    // be infinite.
                    TimeSpan idleTimeout = NetworkConnectionInformation!.Value.IdleTimeout;
                    if (idleTimeout != TimeSpan.MaxValue && idleTimeout != Timeout.InfiniteTimeSpan)
                    {
                        _timer = new Timer(value => Monitor(), null, idleTimeout / 2, idleTimeout / 2);
                    }

                    // Start the receive request task. The task accepts new incoming requests and
                    // processes them. It only completes once the connection is closed.
                    _ = Task.Run(() => AcceptIncomingRequestAsync(Dispatcher ?? NullDispatcher.Instance),
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

        internal void Monitor()
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
                         (Options.KeepAlive || _protocolConnection.HasDispatchesInProgress))
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
        private async Task AcceptIncomingRequestAsync(IDispatcher dispatcher)
        {
            IncomingRequest request;
            try
            {
                request = await _protocolConnection!.ReceiveRequestAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _ = CloseAsync(exception);
                return;
            }

            // Start a new task to accept a new incoming request before dispatching this one.
            _ = Task.Run(() => AcceptIncomingRequestAsync(dispatcher));

            try
            {
                // Dispatch the request and get the response.
                OutgoingResponse? response = null;
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
                        if (Protocol == Protocol.Ice1)
                        {
                            exception = new DispatchException("dispatch canceled by peer");
                        }
                        else
                        {
                            // Rethrow to abort the stream.
                            throw;
                        }
                    }

                    if (exception is not RemoteException remoteException || remoteException.ConvertToUnhandled)
                    {
                        remoteException = new UnhandledException(exception);
                    }

                    if (remoteException.Origin == RemoteExceptionOrigin.Unknown)
                    {
                        remoteException.Origin = new RemoteExceptionOrigin(request.Path, request.Operation);
                    }

                    IceEncoding payloadEncoding = request.GetIceEncoding();
                    ReadOnlyMemory<ReadOnlyMemory<byte>> payload =
                        payloadEncoding.CreatePayloadFromRemoteException(remoteException);

                    response = new OutgoingResponse(Protocol, ResultType.Failure)
                    {
                        Payload = payload,
                        PayloadEncoding = payloadEncoding
                    };

                    if (Protocol.HasFieldSupport && remoteException.RetryPolicy != RetryPolicy.NoRetry)
                    {
                        RetryPolicy retryPolicy = remoteException.RetryPolicy;
                        response.Fields.Add((int)FieldKey.RetryPolicy, encoder => retryPolicy.Encode(encoder));
                    }
                }

                await _protocolConnection.SendResponseAsync(
                    response,
                    request,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                request.Stream?.Abort(MultiplexedStreamError.DispatchCanceled);
            }
            catch (MultiplexedStreamAbortedException)
            {
            }
            catch (Exception exception)
            {
                // Unexpected exception, close the connection.
                await CloseAsync(exception).ConfigureAwait(false);
            }
        }

        /// <summary>Closes the connection. This will forcefully close the connection if the connection hasn't
        /// been shutdown gracefully. Resources allocated for the connection are freed. The connection can be
        /// re-established once this method returns by calling <see cref="ConnectAsync"/>.</summary>
        private async Task CloseAsync(Exception exception)
        {
            lock (_mutex)
            {
                if (_state != ConnectionState.Closed)
                {
                    // It's important to set the state before performing the close. The close of the streams
                    // will trigger the failure of the associated invocations whose interceptor might access
                    // the connection state (e.g.: the retry interceptor or the connection pool checks the
                    // connection state).
                    _state = ConnectionState.Closed;
                    _closeTask = PerformCloseAsync();
                }
            }

            await _closeTask!.ConfigureAwait(false);

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

                _protocolShutdownCancellationSource.Dispose();

                // Raise the Closed event, this will call user code so we shouldn't hold the mutex.
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
}
