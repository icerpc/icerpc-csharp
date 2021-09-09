// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Protocols;
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
        /// <summary>The default value for <see cref="IClientTransport"/>.</summary>
        public static IClientTransport DefaultClientTransport { get; } =
            new ClientTransport().UseTcp().UseColoc();

        /// <summary>The <see cref="IClientTransport"/> used by this connection to create client connections.</summary>
        public IClientTransport ClientTransport { get; init; } = DefaultClientTransport;

        /// <summary>This event is raised when the connection is closed. The connection object is passed as the
        /// event sender argument.</summary>
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

        /// <summary>Gets or sets the dispatcher that dispatches requests received by this connection. For server
        /// connections, set is an invalid operation and get returns the dispatcher of the server that created this
        /// connection. For client connections, set can be called during configuration.</summary>
        /// <value>The dispatcher that dispatches requests received by this connection, or null if no dispatcher is
        /// set.</value>
        /// <exception cref="InvalidOperationException">Thrown if the connection is a server connection.</exception>
        public IDispatcher? Dispatcher
        {
            get => _dispatcher;

            set
            {
                if (IsServer)
                {
                    throw new InvalidOperationException("cannot change the dispatcher of a server connection");
                }
                else
                {
                    _dispatcher = value;
                }
            }
        }

        /// <summary>Gets the connection idle timeout. With Ice2, the IdleTimeout is negotiated when the
        /// connection is established. The lowest IdleTimeout from either the client or server is used.</summary>
        public TimeSpan IdleTimeout => _protocolConnection?.IdleTimeout ?? _options.IdleTimeout;

        /// <summary>The maximum size in bytes of an incoming Ice1 or Ice2 protocol frame.</summary>
        public int IncomingFrameMaxSize => _options.IncomingFrameMaxSize;

        /// <summary><c>true</c> if the connection uses a secure transport, <c>false</c> otherwise.</summary>
        /// <remarks><c>false</c> can mean the connection is not yet connected and its security will be determined
        /// during connection establishment.</remarks>
        public bool IsSecure => TransportConnection?.IsSecure ?? false;

        /// <summary><c>true</c> for a connection accepted by a server and <c>false</c> for a connection created by a
        /// client.</summary>
        public bool IsServer => _localEndpoint != null;

        /// <summary>Whether or not connections are kept alive. If a connection is kept alive, the connection
        /// monitoring will send keep alive frames to ensure the peer doesn't close the connection in the
        /// period defined by its idle timeout. How often keep alive frames are sent depends on the peer's
        /// IdleTimeout configuration. The default value is false.</summary>
        public bool KeepAlive => _options.KeepAlive;

        /// <summary>The connection local endpoint.</summary>
        /// <exception cref="InvalidOperationException">Thrown if the local endpoint is not available.</exception>
        public Endpoint? LocalEndpoint => _localEndpoint ?? TransportConnection?.LocalEndpoint;

        /// <summary>The logger factory to use for creating the connection logger.</summary>
        /// <exception cref="InvalidOperationException">Thrown by the setter if the state of the connection is not
        /// <see cref="ConnectionState.NotConnected"/>.</exception>
        public ILoggerFactory? LoggerFactory
        {
            get => _loggerFactory;
            init
            {
                _loggerFactory = value;
                _logger = (_loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("IceRpc");
            }
        }

        /// <summary>This event is raised when the connection receives a ping frame. The connection object is
        /// passed as the event sender argument.</summary>
        public event EventHandler? PingReceived;

        /// <summary>The protocol used by the connection.</summary>
        public Protocol Protocol => (_localEndpoint ?? _remoteEndpoint)?.Protocol ?? Protocol.Ice2;

        /// <summary>The connection remote endpoint.</summary>
        /// <exception cref="InvalidOperationException">Thrown if the remote endpoint is not available.</exception>
        public Endpoint? RemoteEndpoint
        {
            get => _remoteEndpoint ?? TransportConnection?.RemoteEndpoint;
            init
            {
                Debug.Assert(!IsServer);
                _remoteEndpoint = value;
            }
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

        /// <summary>The transport connection used by this connection.</summary>
        public ITransportConnection? TransportConnection { get; private set; }

        // Delegate used to remove the connection once it has been closed.
        internal Action<Connection>? Remove
        {
            set
            {
                lock (_mutex)
                {
                    // If the connection was closed before the delegate was set execute it immediately otherwise
                    // it will be called once the connection is closed.
                    if (State == ConnectionState.Closed)
                    {
                        Task.Run(() => value?.Invoke(this));
                    }
                    else
                    {
                        _remove = value;
                    }
                }
            }
        }

        // The connect task is assigned when ConnectAsync is called, it's protected with _mutex.
        private Task? _connectTask;
        private EventHandler<ClosedEventArgs>? _closed;
        // The close task is assigned when ShutdownAsync or CloseAsync are called, it's protected with _mutex.
        private Task? _closeTask;
        private IDispatcher? _dispatcher;
        private readonly Endpoint? _localEndpoint;
        private ILogger _logger;
        private ILoggerFactory? _loggerFactory;
        // The mutex protects mutable data members and ensures the logic for some operations is performed atomically.
        private readonly object _mutex = new();
        private readonly ConnectionOptions _options;
        private IProtocolConnection? _protocolConnection;
        private Endpoint? _remoteEndpoint;
        private Action<Connection>? _remove;
        private ConnectionState _state = ConnectionState.NotConnected;
        private Timer? _timer;

        /// <summary>Constructs a new client connection.</summary>
        public Connection() :
            this(options: new())
        {
        }

        /// <summary>Constructs a new client connection with specific options.</summary>
        /// <param name="options">The connection options</param>
        public Connection(ConnectionOptions options)
        {
            _logger = NullLogger.Instance;
            _options = options;
        }

        /// <summary>Closes the connection. This methods switches the connection state to <see
        /// cref="ConnectionState.Closed"/>. If <see cref="Closed"/> event listeners are registered, it waits
        /// for the events to be executed.</summary>
        /// <param name="message">A description of the connection close reason.</param>
        public Task CloseAsync(string? message = null) =>
            CloseAsync(new ConnectionClosedException(message ?? "connection closed forcefully"));

        /// <summary>Establishes the connection to the <see cref="RemoteEndpoint"/>.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A task that indicates the completion of the connect operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the connection is already closed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if <see cref="RemoteEndpoint"/> is not set.</exception>
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
                else if (_state == ConnectionState.NotConnected)
                {
                    if (!IsServer)
                    {
                        Debug.Assert(_protocolConnection == null);

                        if (_remoteEndpoint == null)
                        {
                            throw new InvalidOperationException("client connection has no remote endpoint set");
                        }
                        TransportConnection = ClientTransport.CreateConnection(
                            _remoteEndpoint,
                            _loggerFactory ?? NullLoggerFactory.Instance);
                    }

                    Debug.Assert(TransportConnection != null);
                    _state = ConnectionState.Connecting;

                    // Perform connection establishment.
                    _connectTask = PerformConnectAsync();
                }

                Debug.Assert(_state == ConnectionState.Connecting && _connectTask != null);
            }

            // TODO: Also cancel PerformConnectAsync if the ConnectAsync cancellation token is canceled?
            return _connectTask.WaitAsync(cancel);

            async Task PerformConnectAsync()
            {
                using var connectCancellationSource = new CancellationTokenSource(_options.ConnectTimeout);
                try
                {
                    await Task.Yield();

                    _protocolConnection = new MultiStreamProtocolConnection(
                        (MultiStreamConnection)TransportConnection, // TODO: remove cast
                        _options.IdleTimeout,
                        _options.IncomingFrameMaxSize,
                        PingReceived == null ? null : () =>
                            {
                                Task.Run(() =>
                                {
                                    try
                                    {
                                        PingReceived?.Invoke(this, EventArgs.Empty);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogConnectionEventHandlerException("ping", ex);
                                    }
                                });
                            });

                    await _protocolConnection.InitializeAsync(connectCancellationSource.Token).ConfigureAwait(false);

                    // Start the scope only once the connection is connected/accepted to ensure that the .NET
                    // connection endpoints are available.
                    using IDisposable? scope = _logger.StartConnectionScope(this);
                    lock (_mutex)
                    {
                        if (_state == ConnectionState.Closed)
                        {
                            // This can occur if the connection is disposed while the connection is being
                            // initialized.
                            throw new ConnectionClosedException();
                        }

                        _state = ConnectionState.Active;

                        // Setup a timer to check for the connection idle time every IdleTimeout / 2 period. If the
                        // transport doesn't support idle timeout (e.g.: the colocated transport), IdleTimeout will
                        // be infinite.
                        if (_protocolConnection!.IdleTimeout != Timeout.InfiniteTimeSpan)
                        {
                            TimeSpan period = _protocolConnection.IdleTimeout / 2;
                            _timer = new Timer(value => Monitor(), null, period, period);
                        }

                        // Start a task to wait for graceful shutdown.
                        if (!TransportConnection.IsDatagram)
                        {
                            _ = Task.Run(() => WaitForShutdownAsync(), CancellationToken.None);
                        }

                        // Start the receive request task. The task accepts new incoming requests and processes
                        // them. It only completes once the connection is closed.
                        // TODO: should we allow changing the dispatcher after connection establishment?
                        _ = Task.Run(
                            () => AcceptIncomingRequestAsync(Dispatcher ?? NullDispatcher.Instance),
                            CancellationToken.None);

                        Action logSuccess = (IsServer, TransportConnection.IsDatagram) switch
                        {
                            (false, false) => _logger.LogConnectionEstablished,
                            (false, true) => _logger.LogStartSendingDatagrams,
                            (true, false) => _logger.LogConnectionAccepted,
                            (true, true) => _logger.LogStartReceivingDatagrams
                        };
                        logSuccess();
                    }
                }
                catch (Exception exception)
                {
                    if (exception is OperationCanceledException)
                    {
                        exception = new ConnectTimeoutException();
                    }
                    await CloseAsync(exception).ConfigureAwait(false);
                    throw;
                }
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
            TransportConnection!.HasCompatibleParams(remoteEndpoint);

        /// <inheritdoc/>
        public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // Make sure the connection is connected.
            try
            {
                await ConnectAsync(cancel).ConfigureAwait(false);
            }
            catch
            {
                request.RetryPolicy = RetryPolicy.Immediately;
                throw;
            }

            if (TransportConnection!.IsDatagram && !request.IsOneway)
            {
                throw new InvalidOperationException("cannot send twoway request over datagram connection");
            }

            try
            {
                await _protocolConnection!.SendRequestAsync(request, cancel).ConfigureAwait(false);

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
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                request.Stream.Abort(RpcStreamError.InvocationCanceled);
                throw;
            }
            catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamError.DispatchCanceled)
            {
                throw new OperationCanceledException("dispatch canceled by peer", ex);
            }
            catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamError.ConnectionShutdown)
            {
                // Invocations are canceled immediately when Shutdown is called on the connection.
                throw new OperationCanceledException("connection shutdown", ex);
            }
            catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamError.ConnectionShutdownByPeer)
            {
                // If the peer shuts down the connection, streams which are aborted with this error code are
                // always safe to retry since only streams not processed by the peer are aborted.
                request.RetryPolicy = RetryPolicy.Immediately;
                throw new ConnectionClosedException("connection shutdown by peer", ex);
            }
            catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamError.ConnectionAborted)
            {
                if (request.IsIdempotent || !request.IsSent)
                {
                    // Only retry if it's safe to retry: the request is idempotent or it hasn't been sent.
                    request.RetryPolicy = RetryPolicy.Immediately;
                }
                throw new ConnectionLostException(ex);
            }
            catch (RpcStreamAbortedException ex)
            {
                // Unexpected stream abort. This shouldn't occur unless the peer sends bogus data.
                throw new InvalidDataException($"unexpected stream abort (ErrorCode = {ex.ErrorCode})", ex);
            }
            catch (TransportException ex)
            {
                if (State < ConnectionState.Closing)
                {
                    // Close the connection if the request fails with a transport exception.
                    _ = CloseAsync(ex);
                }
                if (request.IsIdempotent || !request.IsSent)
                {
                    // If the connection is being shutdown, exceptions are expected since the request send or response
                    // receive can fail. If the request is idempotent or hasn't been sent it's safe to retry it.
                    request.RetryPolicy = RetryPolicy.Immediately;
                }
                throw;
            }
        }

        /// <summary>Send the GoAway or CloseConnection frame to initiate the shutdown of the connection. Before
        /// sending the frame, ShutdownAsync first ensures that no new streams are accepted. After sending the frame,
        /// ShutdownAsync waits for the streams to complete, the connection closure from the peer or the close
        /// timeout to close the connection. If ShutdownAsync is canceled, dispatch in progress are canceled and a
        /// GoAwayCanceled frame is sent to the peer to cancel its dispatches as well. Shutdown cancellation can
        /// lead to a speedier shutdown if dispatches are cancelable.</summary>
        /// <param name="message">The message transmitted to the peer with the GoAway frame.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public Task ShutdownAsync(string? message = null, CancellationToken cancel = default) =>
            ShutdownAsync(closedByPeer: false, message ?? "connection closed gracefully", cancel);

        /// <inheritdoc/>
        public override string ToString() => TransportConnection?.ToString() ?? "";

        /// <summary>Constructs a server connection from an accepted connection.</summary>
        internal Connection(
            MultiStreamConnection connection,
            IDispatcher? dispatcher,
            ConnectionOptions options,
            ILoggerFactory? loggerFactory)
        {
            TransportConnection = connection;
            _localEndpoint = connection.LocalEndpoint!;
            _options = options;
            _logger = loggerFactory?.CreateLogger("IceRpc") ?? NullLogger.Instance;
            _dispatcher = dispatcher;
        }

        internal void Monitor()
        {
            lock (_mutex)
            {
                if (_state != ConnectionState.Active)
                {
                    return;
                }
                Debug.Assert(_protocolConnection != null);

                // TODO: Add back
                // TimeSpan idleTime = Time.Elapsed - _protocolConnection!.LastActivity;
                // if (idleTime > _protocolConnection.IdleTimeout / 4 &&
                //     (_options.KeepAlive || _transportConnection.IncomingStreamCount > 0))
                // {
                //     // We send a ping if there was no activity in the last (IdleTimeout / 4) period. Sending a
                //     // ping sooner than really needed is safer to ensure that the receiver will receive the
                //     // ping in time. Sending the ping if there was no activity in the last (IdleTimeout / 2)
                //     // period isn't enough since Monitor is called only every (IdleTimeout / 2) period. We
                //     // also send a ping if dispatch are in progress to notify the peer that we're still alive.
                //     //
                //     // Note that this doesn't imply that we are sending 4 heartbeats per timeout period
                //     // because Monitor is still only called every (IdleTimeout / 2) period.
                //     _ = _protocolConnection.PingAsync(CancellationToken.None);
                // }
                // else if (idleTime > _protocolConnection.IdleTimeout)
                // {
                //     if (_transportConnection.OutgoingStreamCount > 0)
                //     {
                //         // Close the connection if we didn't receive a heartbeat and the connection is idle.
                //         // The server is supposed to send heartbeats when dispatch are in progress.
                //         _ = CloseAsync("connection timed out");
                //     }
                //     else
                //     {
                //         // The connection is idle, close it.
                //         _ = ShutdownAsync("connection idle");
                //     }
                // }
            }
        }

        /// <summary>Accept an incoming request and dispatch it. As soon a new incoming request is accepted
        /// and before dispatching it, a new accept incoming request task is started to allow multiple
        /// incoming requests to be dispatched. The underlying transport might limit the number of concurrent
        /// incoming requests in progress by holding on accepting a new request.</summary>

        private async Task AcceptIncomingRequestAsync(IDispatcher dispatcher)
        {
            IncomingRequest? request = null;
            try
            {
                request = await _protocolConnection!.ReceiveRequestAsync(default).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await CloseAsync(exception).ConfigureAwait(false);
                return;
            }

            if (request == null)
            {
                Debug.Assert(State >= ConnectionState.Closing);
                // TODO: Set closed state?
                return;
            }

            // Start a new task to accept a new incoming request before dispatching this one.
            _ = Task.Run(() => AcceptIncomingRequestAsync(dispatcher));

            try
            {
                CancellationToken cancel = request.Stream.CancelDispatchSource?.Token ?? default;
                request.Connection = this;

                // Dispatch the request and get the response.
                OutgoingResponse? response = null;
                try
                {
                    response = await dispatcher.DispatchAsync(request, cancel).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    if (!request.IsOneway)
                    {
                        response = OutgoingResponse.ForException(request, exception);
                    }
                }

                if (!request.IsOneway)
                {
                    Debug.Assert(response != null);
                    await _protocolConnection.SendResponseAsync(request, response, cancel).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                request.Stream.Abort(RpcStreamError.DispatchCanceled);
            }
            catch (RpcStreamAbortedException exception)
            {
                request.Stream.Abort(exception.ErrorCode);
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

                if (_protocolConnection != null)
                {
                    _protocolConnection.Dispose();

                    // Log the connection closure
                    using IDisposable? scope = _logger.StartConnectionScope(this);
                    bool isDatagram = TransportConnection!.IsDatagram;
                    if (_state == ConnectionState.Connecting)
                    {
                        // If the connection is connecting but not active yet, we print a trace to show that
                        // the connection got connected or accepted before printing out the connection closed
                        // trace.
                        Action<Exception> logFailure = (IsServer, isDatagram) switch
                        {
                            (false, false) => _logger.LogConnectionConnectFailed,
                            (false, true) => _logger.LogStartSendingDatagramsFailed,
                            (true, false) => _logger.LogConnectionAcceptFailed,
                            (true, true) => _logger.LogStartReceivingDatagramsFailed
                        };
                        logFailure(exception);
                    }
                    else
                    {
                        if (isDatagram && IsServer)
                        {
                            _logger.LogStopReceivingDatagrams();
                        }
                        else if (exception is ConnectionClosedException closedException)
                        {
                            _logger.LogConnectionClosed(exception.Message);
                        }
                        else if (_state == ConnectionState.Closing)
                        {
                            _logger.LogConnectionClosed(exception.Message);
                        }
                        else if (exception.IsConnectionLost())
                        {
                            _logger.LogConnectionClosed("connection lost");
                        }
                        else
                        {
                            _logger.LogConnectionClosed(exception.Message, exception);
                        }
                    }
                }

                _timer?.Dispose();

                // Raise the Closed event, this will call user code so we shouldn't hold the mutex.
                try
                {
                    _closed?.Invoke(this, new ClosedEventArgs(exception));
                }
                catch (Exception ex)
                {
                    _logger.LogConnectionEventHandlerException("close", ex);
                }

                // Remove the connection from its factory. This must be called without the connection's mutex
                // locked because the factory needs to acquire an internal mutex and the factory might call on
                // the connection with its internal mutex locked.
                _remove?.Invoke(this);
            }
        }

        /// <summary>Shutdown the connection.</summary>
        private async Task ShutdownAsync(bool closedByPeer, string message, CancellationToken cancel)
        {
            Task shutdownTask;
            lock (_mutex)
            {
                if (_state == ConnectionState.Active && !TransportConnection!.IsDatagram)
                {
                    _state = ConnectionState.Closing;
                    _closeTask ??= PerformShutdownAsync(message);
                }
                shutdownTask = _closeTask ?? CloseAsync(new ConnectionClosedException(message));
            }

            try
            {
                await shutdownTask.WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // Cancel the shutdown if cancellation is requested.
                _protocolConnection!.CancelShutdown();
            }

            await shutdownTask.ConfigureAwait(false);

            async Task PerformShutdownAsync(string message)
            {
                using var closeCancellationSource = new CancellationTokenSource(_options.CloseTimeout);
                try
                {
                    // Shutdown the connection.
                    await _protocolConnection!.ShutdownAsync(
                        closedByPeer,
                        message,
                        closeCancellationSource.Token).ConfigureAwait(false);

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

        /// <summary>Waits for the shutdown of the connection by the peer. Once the peer requested requested
        /// the connection shutdown, shutdown this side of connection.</summary>
        private async Task WaitForShutdownAsync()
        {
            try
            {
                // Wait for the protocol shutdown.
                string message = await _protocolConnection!.WaitForShutdownAsync(
                    CancellationToken.None).ConfigureAwait(false);

                // Shutdown the connection.
                await ShutdownAsync(
                    closedByPeer: true,
                    message,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await CloseAsync(exception).ConfigureAwait(false);
            }
        }
    }
}
