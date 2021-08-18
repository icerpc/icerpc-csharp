// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Net.Security;

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

        /// <summary>Gets the class factory used for instantiating classes decoded from requests or responses.
        /// </summary>
        public IClassFactory? ClassFactory => _options.ClassFactory;

        /// <summary>The <see cref="IClientTransport"/> used by this connection to create client connections.
        /// </summary>
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
        public TimeSpan IdleTimeout => UnderlyingConnection?.IdleTimeout ?? _options.IdleTimeout;

        /// <summary>The maximum size in bytes of an incoming Ice1 or Ice2 protocol frame.</summary>
        public int IncomingFrameMaxSize => _options.IncomingFrameMaxSize;

        /// <summary><c>true</c> if the connection uses a secure transport, <c>false</c> otherwise.</summary>
        /// <remarks><c>false</c> can mean the connection is not yet connected and its security will be determined
        /// during connection establishment.</remarks>
        public bool IsSecure => UnderlyingConnection?.IsSecure ?? false;

        /// <summary><c>true</c> for a connection accepted by a server and <c>false</c> for a connection created by a
        /// client.</summary>
        public bool IsServer => _localEndpoint != null;

        /// <summary>Whether or not connections are kept alive. If a connection is kept alive, the
        /// connection monitoring will send keep alive frames to ensure the peer doesn't close the connection
        /// in the period defined by its idle timeout. How often keep alive frames are sent depends on the
        /// peer's IdleTimeout configuration. The default value is false.</summary>
        public bool KeepAlive => _options.KeepAlive;

        /// <summary>The connection local endpoint.</summary>
        /// <exception cref="InvalidOperationException">Thrown if the local endpoint is not available.</exception>
        public Endpoint? LocalEndpoint => _localEndpoint ?? UnderlyingConnection?.LocalEndpoint;

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

        /// <summary>The client connection options. This property can be used to initialize the client connection options.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Design",
            "CA1044:Properties should not be write only",
            Justification = "Used for initializing the client options")]
        public ClientConnectionOptions Options
        {
            init => _options = value;
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
            get => _remoteEndpoint ?? UnderlyingConnection?.RemoteEndpoint;
            init
            {
                Debug.Assert(!IsServer);
                _remoteEndpoint = value;
            }
        }

        /// <summary>Gets the remote exception factory used for instantiating remote exceptions.</summary>
        public IRemoteExceptionFactory? RemoteExceptionFactory => _options.RemoteExceptionFactory;

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

        /// <summary>The default value for <see cref="EndpointCodex"/>.</summary>
        internal static IEndpointCodex DefaultEndpointCodex { get; } =
            new EndpointCodexBuilder().AddTcp().AddSsl().AddUdp().Build();

        /// <summary>The underlying multi-stream connection.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification = "Disposed by AbortAsync")]
        public MultiStreamConnection? UnderlyingConnection { get; private set; }

        internal int ClassGraphMaxDepth => _options.ClassGraphMaxDepth;

        /// <summary>The endpoint codex is used when encoding or decoding an ice1 endpoint (typically inside a proxy)
        /// with the Ice 1.1 encoding. We need such an encoder/decoder because the Ice 1.1 encoding of endpoints is
        /// transport-dependent.</summary>
        // TODO: provide public API to get/set this codex.
        internal IEndpointCodex EndpointCodex { get; set; } = DefaultEndpointCodex;

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

        private readonly TaskCompletionSource _acceptStreamCompletion = new();
        private TaskCompletionSource? _cancelGoAwaySource;
        private bool _connected;
        private Task? _connectTask;
        // The control stream is assigned on the connection initialization and is immutable once the connection reaches
        // the Active state.
        private RpcStream? _controlStream;
        private EventHandler<ClosedEventArgs>? _closed;
        // The close task is assigned when ShutdownAsync or AbortAsync are called, it's protected with _mutex.
        private Task? _closeTask;
        private IDispatcher? _dispatcher;
        private readonly Endpoint? _localEndpoint;
        private ILogger _logger;
        private ILoggerFactory? _loggerFactory;
        // The mutex protects mutable data members and ensures the logic for some operations is performed atomically.
        private readonly object _mutex = new();
        private ConnectionOptions _options;
        private RpcStream? _peerControlStream;
        private Endpoint? _remoteEndpoint;
        private Action<Connection>? _remove;
        private ConnectionState _state = ConnectionState.NotConnected;
        private Timer? _timer;

        /// <summary>Constructs a new client connection.</summary>
        public Connection()
        {
            _logger = NullLogger.Instance;
            _options = ClientConnectionOptions.Default;
        }

        /// <summary>Aborts the connection. This methods switches the connection state to
        /// <see cref="ConnectionState.Closed"/>. If <see cref="Closed"/> event listeners are registered, it waits for
        /// the events to be executed.</summary>
        /// <param name="message">A description of the connection abortion reason.</param>
        public Task AbortAsync(string? message = null)
        {
            using IDisposable? scope = _logger.StartConnectionScope(this);
            return AbortAsync(new ConnectionClosedException(message ?? "connection closed forcefully"));
        }

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
                else if (_state == ConnectionState.Connecting)
                {
                    return _connectTask!;
                }
                Debug.Assert(_state == ConnectionState.NotConnected);

                ValueTask connectTask;
                if (IsServer)
                {
                    var serverOptions = (ServerConnectionOptions)_options;
                    Debug.Assert(UnderlyingConnection != null);

                    // If the underlying connection is secure, accept with the SSL server authentication options.
                    SslServerAuthenticationOptions? serverAuthenticationOptions = null;
                    if (UnderlyingConnection.IsSecure ?? true)
                    {
                        serverAuthenticationOptions = serverOptions.AuthenticationOptions?.Clone() ?? new();
                        serverAuthenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol>
                        {
                            new SslApplicationProtocol(Protocol.GetName())
                        };
                    }

                    connectTask = UnderlyingConnection.AcceptAsync(serverAuthenticationOptions, cancel);
                }
                else
                {
                    var clientOptions = (ClientConnectionOptions)_options;
                    Debug.Assert(UnderlyingConnection == null);

                    if (_remoteEndpoint == null)
                    {
                        throw new InvalidOperationException("client connection has no remote endpoint set");
                    }
                    UnderlyingConnection = ClientTransport.CreateConnection(
                        _remoteEndpoint,
                        clientOptions,
                        _loggerFactory ?? NullLoggerFactory.Instance);

                    // If the endpoint is secure, connect with the SSL client authentication options.
                    SslClientAuthenticationOptions? clientAuthenticationOptions = null;
                    if (UnderlyingConnection.IsSecure ?? true)
                    {
                        clientAuthenticationOptions = clientOptions.AuthenticationOptions?.Clone() ?? new();
                        clientAuthenticationOptions.TargetHost ??= _remoteEndpoint.Host;
                        clientAuthenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol> {
                            new SslApplicationProtocol(Protocol.GetName())
                        };
                    }

                    connectTask = UnderlyingConnection.ConnectAsync(clientAuthenticationOptions, cancel);
                }

                Debug.Assert(UnderlyingConnection != null);
                _state = ConnectionState.Connecting;

                // Initialize the connection after it's connected.
                _connectTask = PerformInitializeAsync(UnderlyingConnection, connectTask);
            }

            return _connectTask;

            async Task PerformInitializeAsync(MultiStreamConnection connection, ValueTask connectTask)
            {
                try
                {
                    // Wait for the connection to be connected or accepted.
                    await connectTask.ConfigureAwait(false);

                    // Start the scope only once the connection is connected/accepted to ensure that the .NET connection
                    // endpoints are available.
                    using IDisposable? scope = _logger.StartConnectionScope(this);
                    lock (_mutex)
                    {
                        if (_state == ConnectionState.Closed)
                        {
                            // This can occur if the connection is disposed while the connection is being initialized.
                            throw new ConnectionClosedException();
                        }

                        // Set _connected to true to ensure that if AbortAsync is called concurrently, AbortAsync will
                        // trace the correct message.
                        _connected = true;

                        Action logSuccess = (IsServer, connection.IsDatagram) switch
                        {
                            (false, false) => _logger.LogConnectionEstablished,
                            (false, true) => _logger.LogStartSendingDatagrams,
                            (true, false) => _logger.LogConnectionAccepted,
                            (true, true) => _logger.LogStartReceivingDatagrams
                        };
                        logSuccess();
                    }

                    // Initialize the transport.
                    await connection.InitializeAsync(cancel).ConfigureAwait(false);

                    if (!connection.IsDatagram)
                    {
                        // Create the control stream and send the protocol initialize frame
                        _controlStream = await connection.SendInitializeFrameAsync(cancel).ConfigureAwait(false);

                        // Wait for the peer control stream to be accepted and read the protocol initialize frame
                        _peerControlStream = await connection.ReceiveInitializeFrameAsync(cancel).ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    using IDisposable? scope = _logger.StartConnectionScope(this);
                    await AbortAsync(exception).ConfigureAwait(false);
                    throw;
                }

                lock (_mutex)
                {
                    if (_state == ConnectionState.Closed)
                    {
                        // This can occur if the connection is disposed while the connection is being initialized.
                        throw new ConnectionClosedException();
                    }

                    UnderlyingConnection.PingReceived = () =>
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
                    };

                    _state = ConnectionState.Active;

                    // Setup a timer to check for the connection idle time every IdleTimeout / 2 period. If the
                    // transport doesn't support idle timeout (e.g.: the colocated transport), IdleTimeout will
                    // be infinite.
                    if (UnderlyingConnection!.IdleTimeout != Timeout.InfiniteTimeSpan)
                    {
                        TimeSpan period = UnderlyingConnection.IdleTimeout / 2;
                        _timer = new Timer(value => Monitor(), null, period, period);
                    }

                    using IDisposable? scope = _logger.StartConnectionScope(this);

                    // Start a task to wait for the GoAway frame on the peer's control stream.
                    if (!connection.IsDatagram)
                    {
                        _ = Task.Run(async () => await WaitForShutdownAsync().ConfigureAwait(false), default);
                    }

                    // Start the accept stream task. The task accepts new incoming streams and processes them. It only
                    // completes once the connection is closed.
                    _ = Task.Run(() => AcceptStreamAsync(), CancellationToken.None);
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
            UnderlyingConnection!.HasCompatibleParams(remoteEndpoint);

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

            if (UnderlyingConnection!.IsDatagram && !request.IsOneway)
            {
                throw new InvalidOperationException("cannot send twoway request over datagram connection");
            }

            try
            {
                using IDisposable? connectionScope = _logger.StartConnectionScope(this);

                // Create the stream. The caller (the proxy InvokeAsync implementation) is responsible for releasing
                // the stream.
                request.Stream = UnderlyingConnection!.CreateStream(!request.IsOneway);

                // Send the request and wait for the sending to complete.
                await request.Stream.SendRequestFrameAsync(request, cancel).ConfigureAwait(false);

                _logger.LogSentRequestFrame(
                    request.Path,
                    request.Operation,
                    request.PayloadSize,
                    request.PayloadEncoding);

                // Mark the request as sent.
                request.IsSent = true;

                // Wait for the reception of the response.
                IncomingResponse response = request.IsOneway ?
                    new IncomingResponse(this, request.PayloadEncoding) :
                    await request.Stream.ReceiveResponseFrameAsync(cancel).ConfigureAwait(false);

                _logger.LogReceivedResponseFrame(
                    request.Path,
                    request.Operation,
                    response.PayloadSize,
                    response.PayloadEncoding,
                    response.ResultType);

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
                    // Abort the connection if the request fails with a transport exception.
                    _ = AbortAsync(ex);
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

        /// <summary>Sends an asynchronous ping frame.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public async Task PingAsync(CancellationToken cancel = default)
        {
            if (UnderlyingConnection == null)
            {
                throw new InvalidOperationException("connection is not established");
            }
            await UnderlyingConnection.PingAsync(cancel).ConfigureAwait(false);
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
            ShutdownAsync(new ConnectionClosedException(message ?? "connection closed gracefully"), cancel);

        /// <inheritdoc/>
        public override string ToString() => UnderlyingConnection?.ToString() ?? "";

        /// <summary>Constructs a server connection from an accepted connection.</summary>
        internal Connection(
            MultiStreamConnection connection,
            IDispatcher? dispatcher,
            ConnectionOptions options,
            ILoggerFactory? loggerFactory)
        {
            UnderlyingConnection = connection;
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
                Debug.Assert(UnderlyingConnection != null);

                TimeSpan idleTime = Time.Elapsed - UnderlyingConnection!.LastActivity;
                if (idleTime > UnderlyingConnection.IdleTimeout / 4 &&
                    (_options!.KeepAlive || UnderlyingConnection.IncomingStreamCount > 0))
                {
                    // We send a ping if there was no activity in the last (IdleTimeout / 4) period. Sending a ping
                    // sooner than really needed is safer to ensure that the receiver will receive the ping in time.
                    // Sending the ping if there was no activity in the last (IdleTimeout / 2) period isn't enough
                    // since Monitor is called only every (IdleTimeout / 2) period. We also send a ping if dispatch are
                    // in progress to notify the peer that we're still alive.
                    //
                    // Note that this doesn't imply that we are sending 4 heartbeats per timeout period because Monitor
                    // is still only called every (IdleTimeout / 2) period.
                    _ = UnderlyingConnection.PingAsync(CancellationToken.None);
                }
                else if (idleTime > UnderlyingConnection.IdleTimeout)
                {
                    if (UnderlyingConnection.OutgoingStreamCount > 0)
                    {
                        // Close the connection if we didn't receive a heartbeat and the connection is idle. The server
                        // is supposed to send heartbeats when dispatch are in progress.
                        _ = AbortAsync("connection timed out");
                    }
                    else
                    {
                        // The connection is idle, close it.
                        _ = ShutdownAsync("connection idle");
                    }
                }
            }
        }

        private async Task AbortAsync(Exception exception)
        {
            lock (_mutex)
            {
                if (_state != ConnectionState.Closed)
                {
                    // It's important to set the state before performing the abort. The abort of the stream will
                    // trigger the failure of the associated invocations whose interceptor might access the connection
                    // state (e.g.: the retry interceptor or the connection pool check the connection state).
                    _state = ConnectionState.Closed;
                    _closeTask = PerformAbortAsync();
                }
            }

            await _closeTask!.ConfigureAwait(false);

            async Task PerformAbortAsync()
            {
                // Yield before continuing to ensure the code below isn't executed with the mutex locked and that
                // _closeTask is assigned before any synchronous continuations are ran.
                await Task.Yield();

                if (UnderlyingConnection != null)
                {
                    bool isDatagram = UnderlyingConnection.IsDatagram;
                    UnderlyingConnection.Dispose();

                    // Log the connection closure
                    if (!_connected)
                    {
                        // If the connection is connecting but not active yet, we print a trace to show that the
                        // connection got connected or accepted before printing out the connection closed trace.
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
                _cancelGoAwaySource?.TrySetCanceled();

                // Raise the Closed event, this will call user code so we shouldn't hold the mutex.
                try
                {
                    _closed?.Invoke(this, new ClosedEventArgs(exception));
                }
                catch (Exception ex)
                {
                    _logger.LogConnectionEventHandlerException("close", ex);
                }

                // Remove the connection from its factory. This must be called without the connection's mutex locked
                // because the factory needs to acquire an internal mutex and the factory might call on the connection
                // with its internal mutex locked.
                _remove?.Invoke(this);
            }
        }

        private async ValueTask AcceptStreamAsync()
        {
            // Accept a new stream.
            RpcStream? stream = null;
            try
            {
                stream = await UnderlyingConnection!.AcceptStreamAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (ConnectionLostException) when (_controlStream!.WriteCompleted)
            {
                // The control stream has been closed and the peer closed the connection. This indicates graceful
                // connection closure.
                _acceptStreamCompletion.SetResult();
            }
            catch (Exception ex)
            {
                _acceptStreamCompletion.SetException(ex);
                _ = AbortAsync(ex);
            }

            // Start a new accept stream task.
            _ = Task.Run(() => AcceptStreamAsync(), CancellationToken.None);

            // Process the stream from the continuation to avoid a thread-context switch.
            if (stream != null)
            {
                try
                {
                    await ProcessIncomingStreamAsync(stream).ConfigureAwait(false);
                }
                catch (RpcStreamAbortedException ex)
                {
                    stream.Abort(ex.ErrorCode);
                }
                catch (Exception ex)
                {
                    // Unexpected exception, abort the connection.
                    _ = AbortAsync(ex);
                }
            }
        }

        private async Task ProcessIncomingStreamAsync(RpcStream stream)
        {
            // Get the cancellation token for the dispatch. The token is cancelled when the stream is reset by the peer
            // or when the stream is aborted because the connection shutdown is canceled or failed.
            CancellationToken cancel = stream.CancelDispatchSource!.Token;

            // Receives the request frame from the stream.
            IncomingRequest request = await stream.ReceiveRequestFrameAsync(cancel).ConfigureAwait(false);
            request.Connection = this;
            request.Stream = stream;

            _logger.LogReceivedRequestFrame(
                request.Path,
                request.Operation,
                request.PayloadSize,
                request.PayloadEncoding);

            OutgoingResponse? response = null;
            try
            {
                response = await (Dispatcher ?? NullDispatcher.Instance).DispatchAsync(
                    request,
                    cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (Protocol == Protocol.Ice1)
                {
                    // With Ice1, stream reset is not supported so we raise a DispatchException instead.
                    response = new OutgoingResponse(request, new DispatchException("dispatch canceled by peer"));
                }
                else
                {
                    stream.Abort(RpcStreamError.DispatchCanceled);
                }
            }
            catch (Exception exception)
            {
                if (!request.IsOneway)
                {
                    // Convert the exception to an UnhandledException if needed.
                    if (exception is not RemoteException remoteException || remoteException.ConvertToUnhandled)
                    {
                        response = new OutgoingResponse(request, new UnhandledException(exception));
                    }
                    else
                    {
                        response = new OutgoingResponse(request, remoteException);
                    }
                }
            }

            // Send the response if the stream is bidirectional.
            if (response != null && !request.IsOneway)
            {
                try
                {
                    await stream.SendResponseFrameAsync(response).ConfigureAwait(false);
                }
                catch (DispatchException ex)
                {
                    // Send the exception as the response instead of sending the response from the dispatch
                    // This can occur if the response exceeds the peer's incoming frame max size.
                    response = new OutgoingResponse(request, ex);
                    await stream.SendResponseFrameAsync(response).ConfigureAwait(false);
                }

                _logger.LogSentResponseFrame(
                    request.Path,
                    request.Operation,
                    response.PayloadSize,
                    response.PayloadEncoding,
                    response.ResultType);
            }
        }

        private async Task ShutdownAsync(Exception exception, CancellationToken cancel)
        {
            Task shutdownTask;
            lock (_mutex)
            {
                if (_state == ConnectionState.Active && !UnderlyingConnection!.IsDatagram)
                {
                    _state = ConnectionState.Closing;
                    if (Protocol == Protocol.Ice2)
                    {
                        _cancelGoAwaySource = new();
                    }
                    _closeTask ??= PerformShutdownAsync(exception);
                }
                shutdownTask = _closeTask ?? AbortAsync(exception);
            }

            try
            {
                await shutdownTask.WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (Protocol == Protocol.Ice1)
                {
                    // Cancel dispatch if shutdown is canceled.
                    UnderlyingConnection?.CancelDispatch();
                }
                else
                {
                    // Notify the task completion source that shutdown was canceled. PerformShutdownAsync will
                    // send the GoAwayCanceled frame once the GoAway frame has been sent.
                    _cancelGoAwaySource?.TrySetResult();
                }
            }

            await shutdownTask.ConfigureAwait(false);

            async Task PerformShutdownAsync(Exception exception)
            {
                Debug.Assert(UnderlyingConnection != null);

                using IDisposable? scope = _logger.StartConnectionScope(this);
                TimeSpan now = Time.Elapsed;
                try
                {
                    // Shutdown the multi-stream connection to prevent new streams from being created. This is done
                    // before the yield to ensure consistency between the connection shutdown state and the connection
                    // closing State.
                    (long, long) lastIncomingStreamIds = UnderlyingConnection.Shutdown();

                    // Yield before continuing to ensure the code below isn't executed with the mutex locked
                    // and that _closeTask is assigned before any synchronous continuations are ran.
                    await Task.Yield();

                    // Setup a cancellation token source for the close timeout.
                    Debug.Assert(_options!.CloseTimeout != TimeSpan.Zero);
                    using var cancelCloseSource = new CancellationTokenSource(_options.CloseTimeout);
                    CancellationToken cancel = cancelCloseSource.Token;

                    if (Protocol == Protocol.Ice1)
                    {
                        // Abort outgoing streams.
                        UnderlyingConnection.AbortOutgoingStreams(RpcStreamError.ConnectionShutdown);

                        // Wait for incoming streams to complete before sending the CloseConnetion frame. Ice1 doesn't
                        // support sending the largest request ID with the CloseConnection frame. When the peer
                        // receives the CloseConnection frame, it indicates that no more requests will be dispatch and
                        // the peer can therefore cancel remaining pending invocations (which can safely be retried).
                        await UnderlyingConnection.WaitForEmptyIncomingStreamsAsync(cancel).ConfigureAwait(false);
                    }

                    // Write the GoAway frame
                    await _controlStream!.SendGoAwayFrameAsync(lastIncomingStreamIds,
                                                               exception.Message,
                                                               cancel).ConfigureAwait(false);

                    if (Protocol == Protocol.Ice2)
                    {
                        // GoAway frame is sent, we can allow shutdown cancellation to send the GoAwayCanceled frame
                        // at this point.
                        _ = PerformCancelGoAwayIfShutdownCanceledAsync();
                    }

                    // Wait for all the streams to complete.
                    await WaitForEmptyStreamsAsync(cancel).ConfigureAwait(false);

                    // Abort the control stream. The peer is supposed to close the connection upon getting the
                    // control stream abortion notification.
                    _controlStream.AbortWrite(RpcStreamError.ConnectionShutdown);

                    // Wait for peer to close the connection.
                    try
                    {
                        await _acceptStreamCompletion.Task.WaitAsync(cancel).ConfigureAwait(false);
                    }
                    catch (TransportException)
                    {
                        // Ignore
                    }

                    // Abort the connection if the peer closed the connection.
                    await AbortAsync(exception).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    var ex = new ConnectionClosedException("connection closure timed out");
                    await AbortAsync(ex).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await AbortAsync(ex).ConfigureAwait(false);
                }
            }

            async Task PerformCancelGoAwayIfShutdownCanceledAsync()
            {
                // Wait for the shutdown cancellation.
                await _cancelGoAwaySource!.Task.ConfigureAwait(false);

                // Write the GoAwayCanceled frame to the peer's streams.
                await _controlStream!.SendGoAwayCanceledFrameAsync().ConfigureAwait(false);

                // Cancel dispatch if shutdown is canceled.
                UnderlyingConnection!.CancelDispatch();
            }
        }

        private async Task WaitForEmptyStreamsAsync(CancellationToken cancel)
        {
            // Wait for all the streams to complete or an unexpected connection closure.
            Task waitForEmptyStreams = UnderlyingConnection!.WaitForEmptyStreamsAsync(cancel);
            if (!waitForEmptyStreams.IsCompleted)
            {
                Task waitForClose = _acceptStreamCompletion.Task.WaitAsync(cancel);
                Task task = await Task.WhenAny(waitForEmptyStreams, waitForClose).ConfigureAwait(false);
                if (task == waitForClose)
                {
                    // Check the result of the connection closure. This will raise if the connection wasn't
                    // closed gracefully.
                    await waitForClose.ConfigureAwait(false);

                    // If the peer gracefully closed the connection, we continue waiting for the streams
                    // to complete.
                    await waitForEmptyStreams.ConfigureAwait(false);
                }
            }
        }

        /// <summary>Waits for the GoAway or CloseConnection frame to initiate the shutdown of the connection.
        /// The shutdown of the connection aborts outgoing streams that the peer didn't process yet and waits
        /// for all the stream to complete (or the peer to close the connection). Once all the streams are completed,
        /// the connection is closed.</summary>
        private async Task WaitForShutdownAsync()
        {
            Debug.Assert(State >= ConnectionState.Active);

            // Wait to receive the GoAway frame on the control stream.
            ((long, long) lastOutgoingStreamIds, string message) =
                await _peerControlStream!.ReceiveGoAwayFrameAsync().ConfigureAwait(false);

            Task shutdownTask;
            lock (_mutex)
            {
                var exception = new ConnectionClosedException(message);
                if (_state == ConnectionState.Active)
                {
                    _state = ConnectionState.Closing;
                    shutdownTask = PerformShutdownAsync(lastOutgoingStreamIds, exception, true);
                }
                else if (_state == ConnectionState.Closing)
                {
                    // We already initiated graceful connection closure. If the peer did as well, we can cancel
                    // incoming/outgoing streams.
                    shutdownTask = PerformShutdownAsync(lastOutgoingStreamIds, exception, false);
                }
                else
                {
                    shutdownTask = _closeTask!;
                }
            }

            await shutdownTask.ConfigureAwait(false);

            async Task PerformShutdownAsync((long, long) lastOutgoingStreamIds, Exception exception, bool closing)
            {
                // Shutdown the multi-stream connection to prevent new streams from being created. This is done
                // before the yield to ensure consistency between the connection shutdown state and the connection
                // closing State.
                (long, long) lastIncomingStreamIds = UnderlyingConnection!.Shutdown();

                // Yield before continuing to ensure the code below isn't executed with the mutex locked.
                await Task.Yield();

                // Abort non-processed outgoing streams before closing the connection to ensure the invocations
                // will fail with a retryable exception.
                UnderlyingConnection.AbortOutgoingStreams(RpcStreamError.ConnectionShutdownByPeer,
                                                          lastOutgoingStreamIds);

                try
                {
                    Debug.Assert(_options!.CloseTimeout != TimeSpan.Zero);
                    using var cancelCloseSource = new CancellationTokenSource(_options.CloseTimeout);
                    CancellationToken cancel = cancelCloseSource.Token;

                    if (Protocol == Protocol.Ice1)
                    {
                        Debug.Assert(UnderlyingConnection.IncomingStreamCount == 0 &&
                                     UnderlyingConnection.OutgoingStreamCount == 0);

                        // Abort the connection, all the streams have completed.
                        await AbortAsync(exception).ConfigureAwait(false);
                    }
                    else
                    {
                        if (closing)
                        {
                            // Send back a GoAway frame if we just switched to the closing state. If we were already
                            // in the closing state, it has already been sent.
                            await _controlStream!.SendGoAwayFrameAsync(lastIncomingStreamIds,
                                                                       exception.Message,
                                                                       cancel).ConfigureAwait(false);
                        }

                        // Wait for the GoAwayCanceled frame from the peer or the closure of the peer control stream.
                        Task waitForGoAwayCanceledTask = WaitForGoAwayCanceledOrCloseAsync(cancel);

                        // Wait for all the streams to complete.
                        await WaitForEmptyStreamsAsync(cancel).ConfigureAwait(false);

                        // Wait for the closure of the peer control stream.
                        await waitForGoAwayCanceledTask.ConfigureAwait(false);

                        // Abort the connection.
                        await AbortAsync(exception).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    var ex = new ConnectionClosedException("connection closure timed out");
                    await AbortAsync(ex).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // If the connection is closed
                    await AbortAsync(ex).ConfigureAwait(false);
                }
            }

            async Task WaitForGoAwayCanceledOrCloseAsync(CancellationToken cancel)
            {
                try
                {
                    // Wait to receive the GoAwayCanceled frame.
                    await _peerControlStream!.ReceiveGoAwayCanceledFrameAsync(cancel).ConfigureAwait(false);

                    // Cancel the dispatch if the peer canceled the shutdown.
                    UnderlyingConnection!.CancelDispatch();
                }
                catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamError.ConnectionShutdown)
                {
                    // Expected if the connection is shutdown.
                }
            }
        }
    }
}
