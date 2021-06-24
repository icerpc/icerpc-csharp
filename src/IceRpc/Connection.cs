// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The base class for connection information.</summary>
    public abstract class ConnectionInformation
    {
        /// <summary><c>true</c> if the connection uses a secure transport, <c>false</c> otherwise.</summary>
        public abstract bool IsSecure { get; }

        /// <inheritdoc/>
        public override string ToString() => $"IsSecure={IsSecure}";
    }

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
        /// connection before to switch to the <c>Closed</c> state. The peer while close its end of the connection
        /// only once its dispatch complete.</summary>
        Closing,
        /// <summary>The connection is closed.</summary>
        Closed
    }

    /// <summary>Error codes for connection errors.</summary>
    public enum ConnectionErrorCode : byte
    {
        /// <summary>The connection has been shutdown.</summary>
        Shutdown,
    }

    /// <summary>The ClosedEventArgument class is provided to closed event handlers.</summary>
    public sealed class ClosedEventArgs : EventArgs
    {
        /// <summary>The exception responsible for the connection closure.</summary>
        public Exception Exception { get; }

        internal ClosedEventArgs(Exception exception) => Exception = exception;
    }

    /// <summary>Represents a connection used to send and receive Ice frames.</summary>
    public sealed class Connection : IAsyncDisposable
    {
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

        /// <summary>The dispatcher that a connection calls when its dispatcher is null.</summary>
        internal static IDispatcher NullDispatcher { get; } =
            new InlineDispatcher((request, cancel) => throw new ServiceNotFoundException(RetryPolicy.OtherReplica));

        /// <summary>Gets information about the underlying network connection.</summary>
        /// <exception cref="InvalidOperationException">Thrown if the connection is not connected.</exception>
        public ConnectionInformation ConnectionInformation => _connection?.ConnectionInformation ??
            throw new InvalidOperationException("the connection is not established");

        /// <summary>Gets or sets the dispatcher that dispatches requests received by this connection. For incoming
        /// connections, set is an invalid operation and get returns the dispatcher of the server that created this
        /// connection. For client connections, set can be called during configuration.</summary>
        /// <value>The dispatcher that dispatches requests received by this connection, or null if no dispatcher is
        /// set.</value>
        /// <exception cref="InvalidOperationException">Thrown if the connection is a server connection.</exception>
        public IDispatcher? Dispatcher
        {
            get => Server?.Dispatcher ?? _dispatcher;

            set
            {
                if (Server == null)
                {
                    _dispatcher = value;
                }
                else
                {
                    throw new InvalidOperationException("cannot change the dispatcher of a server connection");
                }
            }
        }

        // TODO: add this when we add support for connection features. Depending on what to do with
        // connection options we might need to copy the features from the options if the features
        // are not readonly.
        // /// <summary>The features of this connection.</summary>
        // public FeatureCollection Features => _options?.Features ?? throw new InvalidOperationException();

        /// <summary>Gets the connection idle timeout. With Ice2, the IdleTimeout is negotiated when the
        /// connection is established. The lowest IdleTimeout from either the client or server is used.</summary>
        public TimeSpan IdleTimeout => _connection?.IdleTimeout ?? _options?.IdleTimeout ?? TimeSpan.Zero;

        /// <summary>Returns <c>true</c> if the connection is active. Outgoing streams can be created and incoming
        /// streams accepted when the connection is active. The connection is no longer considered active as soon
        /// as <see cref="ShutdownAsync(string?, CancellationToken)"/> is called to initiate a graceful connection
        /// closure.</summary>
        /// <return><c>true</c> if the connection is in the <c>ConnectionState.Active</c> state, <c>false</c>
        /// otherwise.</return>
        public bool IsActive => State == ConnectionState.Active;

        /// <summary><c>true</c> for datagram connections <c>false</c> otherwise.</summary>
        public bool IsDatagram => (_localEndpoint ?? _remoteEndpoint)?.IsDatagram ?? false;

        /// <summary><c>true</c> if the connection uses a secure transport, <c>false</c> otherwise.</summary>
        /// <exception cref="InvalidOperationException">Thrown if the connection is not connected.</exception>
        public bool IsSecure => ConnectionInformation.IsSecure;

        /// <summary><c>true</c> for a connection accepted by a server and <c>false</c> for a connection created by a
        /// client.</summary>
        public bool IsServer => _localEndpoint != null;

        /// <summary>The connection local endpoint.</summary>
        /// <exception cref="InvalidOperationException">Thrown if the local endpoint is not available.</exception>
        public Endpoint? LocalEndpoint
        {
            get => _localEndpoint ?? _connection?.LocalEndpoint;
            internal set => _localEndpoint = value;
        }

        /// <summary>The logger factory to use for creating the connection logger.</summary>
        /// <exception cref="InvalidOperationException">Thrown by the setter if the state of the connection is not
        /// <c>ConnectionState.NotConnected</c>.</exception>
        public ILoggerFactory? LoggerFactory
        {
            get => _loggerFactory;
            set
            {
                if (_state > ConnectionState.NotConnected)
                {
                    throw new InvalidOperationException(
                        $"cannot change the connection's logger factory after calling {nameof(ConnectAsync)}");
                }
                _loggerFactory = value;
            }
        }

        /// <summary>The connection options.</summary>
        /// <exception cref="InvalidOperationException">Thrown by the setter if the state of the connection is not
        /// <c>ConnectionState.NotConnected</c>.</exception>
        public ConnectionOptions? Options
        {
            get => _options?.Clone();
            set
            {
                if (_state > ConnectionState.NotConnected)
                {
                    throw new InvalidOperationException(
                        $"cannot change the connection's options after calling {nameof(ConnectAsync)}");
                }
                if (value == null)
                {
                    throw new ArgumentException($"{nameof(value)} can't be null");
                }
                _options = value.Clone();
            }
        }

        /// <summary>The peer's incoming frame maximum size. This is not supported with ice1 connections.</summary>
        /// <exception cref="InvalidOperationException">Thrown if the connection is not connected.</exception>
        /// <exception cref="NotSupportedException">Thrown if the connection is an ice1 connection.</exception>
        public int PeerIncomingFrameMaxSize
        {
            get
            {
                if (Protocol == Protocol.Ice1)
                {
                    throw new NotSupportedException("the peer incoming frame max size is not available with ice1");
                }
                else if (State < ConnectionState.Active)
                {
                    throw new InvalidOperationException("the connection is not connected");
                }
                return _connection!.PeerIncomingFrameMaxSize!.Value;
            }
        }

        /// <summary>This event is raised when the connection receives a ping frame. The connection object is
        /// passed as the event sender argument.</summary>
        public event EventHandler? PingReceived;

        /// <summary>The protocol used by the connection.</summary>
        public Protocol Protocol => (_localEndpoint ?? _remoteEndpoint)?.Protocol ?? Protocol.Ice2;

        /// <summary>The connection remote endpoint.</summary>
        /// <exception cref="InvalidOperationException">Thrown if the remote endpoint is not available or if setting
        /// the remote endpoint is not allowed (the connection is connected or it's a server connection).</exception>
        public Endpoint? RemoteEndpoint
        {
            get => _remoteEndpoint ?? _connection?.RemoteEndpoint;
            set
            {
                if (_state > ConnectionState.NotConnected)
                {
                    throw new InvalidOperationException(
                        $"cannot change the connection's remote endpoint after calling {nameof(ConnectAsync)}");
                }
                _remoteEndpoint = value;
            }
        }

        /// <summary>The server that created this server connection.</summary>
        /// <exception cref="InvalidOperationException">Thrown by the setter if the state of the connection is not
        /// <c>ConnectionState.NotConnected</c>.</exception>
        public Server? Server
        {
            get => _server;
            set
            {
                if (_state > ConnectionState.NotConnected)
                {
                    throw new InvalidOperationException(
                        $"cannot change the connection's server after calling {nameof(ConnectAsync)}");
                }
                _server = value;
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

        /// <summary>The connection transport.</summary>
        /// <exception cref="InvalidOperationException">Thrown if there's no endpoint set.</exception>
        public Transport Transport =>
            (_localEndpoint ?? _remoteEndpoint)?.Transport ??
            throw new InvalidOperationException(
                $"{nameof(Transport)} is not available because there's no endpoint set");

        /// <summary>The connection transport name.</summary>
        /// <exception cref="InvalidOperationException">Thrown if there's no endpoint set.</exception>
        public string TransportName =>
            (_localEndpoint ?? _remoteEndpoint)?.TransportName ??
            throw new InvalidOperationException(
                $"{nameof(TransportName)} is not available because there's no endpoint set");

        internal int ClassGraphMaxDepth => _options!.ClassGraphMaxDepth;

        internal ILogger Logger
        {
            get => _logger ??= (_loggerFactory ?? Runtime.DefaultLoggerFactory).CreateLogger("IceRpc");
            set => _logger = value;
        }

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

        // The accept stream task is assigned each time a new accept stream async operation is started.
        private volatile Task _acceptStreamTask = Task.CompletedTask;
        private TaskCompletionSource? _cancelGoAwaySource;
        private bool _connected;
        private MultiStreamConnection? _connection;
        private Task? _connectTask;
        // The control stream is assigned on the connection initialization and is immutable once the connection
        // reaches the Active state.
        private RpcStream? _controlStream;
        private EventHandler<ClosedEventArgs>? _closed;
        // The close task is assigned when ShutdownAsync or AbortAsync are called, it's protected with _mutex.
        private Task? _closeTask;
        private IDispatcher? _dispatcher;
        private Endpoint? _localEndpoint;
        private ILogger? _logger;
        private ILoggerFactory? _loggerFactory;
        // The mutex protects mutable non-volatile data members and ensures the logic for some operations is
        // performed atomically.
        private readonly object _mutex = new();
        private ConnectionOptions? _options;
        private RpcStream? _peerControlStream;
        private Endpoint? _remoteEndpoint;
        private Action<Connection>? _remove;
        private Server? _server;
        private ConnectionState _state = ConnectionState.NotConnected;
        private Timer? _timer;

        /// <summary>Constructs a new client connection.</summary>
        public Connection()
        {
        }

        /// <summary>Aborts the connection. This methods switches the connection state to <c>ConnectionState.Closed</c>
        /// If <c>Closed</c> event listeners are registered, it waits for the events to be executed.</summary>
        /// <param name="message">A description of the connection abortion reason.</param>
        public Task AbortAsync(string? message = null)
        {
            using IDisposable? scope = _connection?.StartScope(Server);
            return AbortAsync(new ConnectionClosedException(message ?? "connection closed forcefully"));
        }

        /// <summary>Establishes the connection to the <see cref="RemoteEndpoint"/>.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A task that indicates the completion of the connect operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the connection is already closed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if <see cref="RemoteEndpoint"/> is not set or if
        /// <see cref="Options"/> is set to an <see cref="ServerConnectionOptions"/> instance</exception>
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

                _options ??= IsServer ? ServerConnectionOptions.Default : ClientConnectionOptions.Default;
                ValueTask connectTask;
                if (_options is ClientConnectionOptions clientOptions)
                {
                    if (IsServer)
                    {
                        throw new InvalidOperationException(
                            "invalid client connection options for server connection");
                    }

                    if (_connection == null)
                    {
                        if (_remoteEndpoint == null)
                        {
                            throw new InvalidOperationException("client connection has no remote endpoint set");
                        }
                        else if (_localEndpoint != null)
                        {
                            throw new InvalidOperationException("client connection has local endpoint set");
                        }

                        if (_remoteEndpoint.TransportDescriptor?.Connector is
                            Func<Endpoint, ClientConnectionOptions, ILogger, MultiStreamConnection> connector)
                        {
                            _connection = connector(_remoteEndpoint, clientOptions, Logger);
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"cannot create client connection for remote endpoint '{_remoteEndpoint}'");
                        }
                    }

                    // If the endpoint is secure, connect with the SSL client authentication options.
                    SslClientAuthenticationOptions? clientAuthenticationOptions = null;
                    if (_connection.RemoteEndpoint.IsSecure ?? true)
                    {
                        clientAuthenticationOptions = clientOptions.AuthenticationOptions?.Clone() ?? new();
                        clientAuthenticationOptions.TargetHost ??= _connection.RemoteEndpoint.Host;
                        clientAuthenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol> {
                            new SslApplicationProtocol(Protocol.GetName())
                        };
                    }

                    connectTask = _connection.ConnectAsync(clientAuthenticationOptions, cancel);
                }
                else
                {
                    var serverOptions = (ServerConnectionOptions)_options;
                    if (!IsServer)
                    {
                        throw new InvalidOperationException(
                            "invalid server connection options for client connection");
                    }
                    else if (_connection == null)
                    {
                        throw new InvalidOperationException(
                            $"server connection can only be created by a {nameof(Server)}");
                    }

                    // If the endpoint is secure, accept with the SSL server authentication options.
                    SslServerAuthenticationOptions? serverAuthenticationOptions = null;
                    if (_connection.LocalEndpoint.IsSecure ?? true)
                    {
                        serverAuthenticationOptions = serverOptions.AuthenticationOptions?.Clone() ?? new();
                        serverAuthenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol> {
                            new SslApplicationProtocol(Protocol.GetName())
                        };
                    }

                    connectTask = _connection.AcceptAsync(serverAuthenticationOptions, cancel);
                }

                Debug.Assert(_connection != null);
                _state = ConnectionState.Connecting;

                // Initialize the connection after it's connected.
                _connectTask = PerformInitializeAsync(_connection, connectTask);
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
                    using IDisposable? scope = connection.StartScope(Server);
                    lock (_mutex)
                    {
                        if (_state == ConnectionState.Closed)
                        {
                            // This can occur if the communicator or server is disposed while the connection is being
                            // initialized.
                            throw new ConnectionClosedException();
                        }

                        // Set _connected to true to ensure that if AbortAsync is called concurrently, AbortAsync will
                        // trace the correct message.
                        _connected = true;

                        Action logSuccess = (IsServer, IsDatagram) switch
                        {
                            (false, false) => _connection.Logger.LogConnectionEstablished,
                            (false, true) => _connection.Logger.LogStartSendingDatagrams,
                            (true, false) => _connection.Logger.LogConnectionAccepted,
                            (true, true) => _connection.Logger.LogStartReceivingDatagrams
                        };
                        logSuccess();
                    }

                    // Initialize the transport.
                    await connection.InitializeAsync(cancel).ConfigureAwait(false);

                    if (!IsDatagram)
                    {
                        // Create the control stream and send the protocol initialize frame
                        _controlStream = await connection.SendInitializeFrameAsync(cancel).ConfigureAwait(false);

                        // Wait for the peer control stream to be accepted and read the protocol initialize frame
                        _peerControlStream = await connection.ReceiveInitializeFrameAsync(cancel).ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    using IDisposable? scope = connection.StartScope(Server);
                    await AbortAsync(exception).ConfigureAwait(false);
                    throw;
                }

                lock (_mutex)
                {
                    if (_state == ConnectionState.Closed)
                    {
                        // This can occur if the communicator or server is disposed while the connection is being
                        // initialized.
                        throw new ConnectionClosedException();
                    }

                    _connection.PingReceived = () =>
                    {
                        Task.Run(() =>
                        {
                            try
                            {
                                PingReceived?.Invoke(this, EventArgs.Empty);
                            }
                            catch (Exception ex)
                            {
                                Logger.LogConnectionEventHandlerException("ping", ex);
                            }
                        });
                    };

                    _state = ConnectionState.Active;

                    // Setup a timer to check for the connection idle time every IdleTimeout / 2 period. If the
                    // transport doesn't support idle timeout (e.g.: the colocated transport), IdleTimeout will
                    // be infinite.
                    if (_connection!.IdleTimeout != Timeout.InfiniteTimeSpan)
                    {
                        TimeSpan period = _connection.IdleTimeout / 2;
                        _timer = new Timer(value => Monitor(), null, period, period);
                    }

                    using IDisposable? scope = connection.StartScope(Server);

                    // Start a task to wait for the GoAway frame on the peer's control stream.
                    if (!IsDatagram)
                    {
                        _ = Task.Run(async () => await WaitForShutdownAsync().ConfigureAwait(false), default);
                    }

                    // Start the asynchronous AcceptStream operation from the thread pool to prevent reading
                    // synchronously new frames from this thread.
                    _acceptStreamTask = Task.Run(() => AcceptStreamAsync(), default);
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

        /// <inheritdoc/>
        public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            if (IsDatagram && !request.IsOneway)
            {
                throw new InvalidOperationException("cannot send twoway request over datagram connection");
            }

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

            RpcStream? stream = null;
            try
            {
                using IDisposable? connectionScope = StartScope();

                // Create the outgoing stream.
                stream = _connection!.CreateStream(!request.IsOneway);

                // Send the request and wait for the sending to complete.
                await stream.SendRequestFrameAsync(request, cancel).ConfigureAwait(false);

                // Mark the request as sent.
                request.IsSent = true;

                using IDisposable? streamScope = stream.StartScope();

                // Wait for the reception of the response.
                IncomingResponse response = request.IsOneway ?
                    new IncomingResponse(this, request.PayloadEncoding) :
                    await stream.ReceiveResponseFrameAsync(cancel).ConfigureAwait(false);
                response.Connection = this;
                response.Stream = stream;
                return response;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                stream!.Reset(RpcStreamErrorCode.InvocationCanceled);
                throw;
            }
            catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamErrorCode.DispatchCanceled)
            {
                throw new OperationCanceledException("dispatch canceled by peer");
            }
            catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamErrorCode.ConnectionShutdown)
            {
                // Invocations are canceled immediately when Shutdown is called on the connection.
                Debug.Assert(Protocol == Protocol.Ice1);
                throw new OperationCanceledException("connection shutdown");
            }
            catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamErrorCode.ConnectionShutdownByPeer)
            {
                // If the peer shuts down the connection, streams which are aborted with this error code are
                // always safe to retry since only streams not processed by the peer are aborted.
                request.RetryPolicy = RetryPolicy.Immediately;
                throw new ConnectionClosedException("connection shutdown by peer");
            }
            catch (RpcStreamAbortedException ex)
            {
                if (request.IsIdempotent || !request.IsSent)
                {
                    // Only retry if it's safe to retry: the request is idempotent or it hasn't been sent.
                    request.RetryPolicy = RetryPolicy.Immediately;
                }
                Debug.Assert(ex.ErrorCode == RpcStreamErrorCode.ConnectionAborted);
                throw new ConnectionLostException();
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
                    // If the connection is being shutdown, exceptions are expected since the request send
                    // or response receive can fail. If the request is idempotent or hasn't been sent it's
                    // safe to retry it.
                    request.RetryPolicy = RetryPolicy.Immediately;
                }
                throw;
            }
            finally
            {
                // Release one ref-count
                stream?.Release();
            }
        }

        /// <summary>Sends an asynchronous ping frame.</summary>
        /// <param name="progress">Sent progress provider.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public async Task PingAsync(IProgress<bool>? progress = null, CancellationToken cancel = default)
        {
            if (_connection == null)
            {
                throw new InvalidOperationException("connection is not established");
            }
            await _connection.PingAsync(cancel).ConfigureAwait(false);
            progress?.Report(true);
        }

        /// <summary>Shuts down gracefully the connection by sending a GoAway frame to the peer.</summary>
        /// <param name="message">The message transmitted to the peer with the GoAway frame.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public Task ShutdownAsync(string? message = null, CancellationToken cancel = default) =>
            ShutdownAsync(new ConnectionClosedException(message ?? "connection closed gracefully"), cancel);

        /// <summary>Returns a description of the connection as human readable text, suitable for debugging.</summary>
        /// <returns>The description of the connection as human readable text.</returns>
        // TODO: get on ConnectionInformation can throw!
        public override string ToString() => ConnectionInformation == null ? "" :
            $"{ConnectionInformation.GetType().FullName} ({ConnectionInformation}, IsServer={IsServer})";

        /// <summary>Constructs a server connection from an accepted connection.</summary>
        internal Connection(MultiStreamConnection connection, Server server)
        {
            _connection = connection;
            _localEndpoint = connection.LocalEndpoint!;

            Options = server.ConnectionOptions;
            Logger = server.Logger;
            Server = server;
        }

        internal void Monitor()
        {
            lock (_mutex)
            {
                if (_state != ConnectionState.Active)
                {
                    return;
                }
                Debug.Assert(_connection != null);

                TimeSpan idleTime = Time.Elapsed - _connection!.LastActivity;
                if (idleTime > _connection.IdleTimeout / 4 && (_options!.KeepAlive || _connection.IncomingStreamCount > 0))
                {
                    // We send a ping if there was no activity in the last (IdleTimeout / 4) period. Sending a ping
                    // sooner than really needed is safer to ensure that the receiver will receive the ping in
                    // time. Sending the ping if there was no activity in the last (IdleTimeout / 2) period isn't
                    // enough since Monitor is called only every (IdleTimeout / 2) period. We also send a ping if
                    // dispatch are in progress to notify the peer that we're still alive.
                    //
                    // Note that this doesn't imply that we are sending 4 heartbeats per timeout period because
                    // Monitor is still only called every (IdleTimeout / 2) period.
                    _ = _connection.PingAsync(CancellationToken.None);
                }
                else if (idleTime > _connection.IdleTimeout)
                {
                    if (_connection.OutgoingStreamCount > 0)
                    {
                        // Close the connection if we didn't receive a heartbeat and the connection is idle. The
                        // server is supposed to send heartbeats when dispatch are in progress.
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

        internal IDisposable? StartScope() => _connection?.StartScope();

        private async Task AbortAsync(Exception exception)
        {
            lock (_mutex)
            {
                if (_state != ConnectionState.Closed)
                {
                    // It's important to set the state before performing the abort. The abort of the stream
                    // will trigger the failure of the associated invocations whose interceptor might access
                    // the connection state (e.g.: the retry interceptor or the connection pool which calls
                    // IsActive on the connection).
                    _state = ConnectionState.Closed;
                    _closeTask = PerformAbortAsync();
                }
            }

            await _closeTask!.ConfigureAwait(false);

            async Task PerformAbortAsync()
            {
                // Yield before continuing to ensure the code below isn't executed with the mutex locked
                // and that _closeTask is assigned before any synchronous continuations are ran.
                await Task.Yield();

                if (_connection != null)
                {
                    // Abort the streams.
                    _connection.AbortStreams(RpcStreamErrorCode.ConnectionAborted);

                    _connection.Dispose();

                    // Log the connection closure
                    if (!_connected)
                    {
                        // If the connection is connecting but not active yet, we print a trace to show that
                        // the connection got connected or accepted before printing out the connection closed
                        // trace.
                        Action<Exception> logFailure = (IsServer, IsDatagram) switch
                        {
                            (false, false) => _connection.Logger.LogConnectionConnectFailed,
                            (false, true) => _connection.Logger.LogStartSendingDatagramsFailed,
                            (true, false) => _connection.Logger.LogConnectionAcceptFailed,
                            (true, true) => _connection.Logger.LogStartReceivingDatagramsFailed
                        };
                        logFailure(exception);
                    }
                    else
                    {
                        if (IsDatagram && IsServer)
                        {
                            _connection.Logger.LogStopReceivingDatagrams();
                        }
                        else if (exception is ConnectionClosedException closedException)
                        {
                            _connection.Logger.LogConnectionClosed(exception.Message);
                        }
                        else if (_state == ConnectionState.Closing)
                        {
                            _connection.Logger.LogConnectionClosed(exception.Message);
                        }
                        else if (exception.IsConnectionLost())
                        {
                            _connection.Logger.LogConnectionClosed("connection lost");
                        }
                        else
                        {
                            _connection.Logger.LogConnectionClosed(exception.Message, exception);
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
                    Logger.LogConnectionEventHandlerException("close", ex);
                }

                // Remove the connection from its factory. This must be called without the connection's mutex locked
                // because the factory needs to acquire an internal mutex and the factory might call on the connection
                // with its internal mutex locked.
                _remove?.Invoke(this);
            }
        }

        private async Task AcceptStreamAsync()
        {
            RpcStream? stream = null;
            try
            {
                // Accept a new stream.
                stream = await _connection!.AcceptStreamAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (State == ConnectionState.Closing || ex is ConnectionClosedException)
            {
                // The connection is being closed gracefully, let the exception go through.
                throw;
            }
            catch (Exception ex)
            {
                _ = AbortAsync(ex);
                throw;
            }

            // Start a new accept stream task to accept another stream.
            _acceptStreamTask = Task.Run(() => AcceptStreamAsync());

            using IDisposable? streamScope = stream.StartScope();

            Debug.Assert(stream != null);
            try
            {
                // Get the cancellation token for the dispatch. The token is cancelled when the stream is reset by the
                // peer or when the stream is aborted because the connection shutdown is canceled or failed.
                CancellationToken cancel = stream.CancelDispatchSource!.Token;

                // Receives the request frame from the stream
                IncomingRequest request = await stream.ReceiveRequestFrameAsync(cancel).ConfigureAwait(false);
                request.Connection = this;
                request.Stream = stream;

                OutgoingResponse? response = null;

                try
                {
                    response =
                        await (Dispatcher ?? NullDispatcher).DispatchAsync(request, cancel).ConfigureAwait(false);
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
                        stream.Reset(RpcStreamErrorCode.DispatchCanceled);
                    }
                }
                catch (Exception exception)
                {
                    // Convert the exception to an UnhandledException if needed.
                    if (exception is not RemoteException remoteException || remoteException.ConvertToUnhandled)
                    {
                        // We log the exception as the UnhandledException may not include all details.
                        _connection!.Logger.LogDispatchException(request.Connection,
                                                                 request.Path,
                                                                 request.Operation,
                                                                 exception);
                        response = new OutgoingResponse(request, new UnhandledException(exception));
                    }
                    else if (!stream.IsBidirectional)
                    {
                        // We log this exception, otherwise it would be lost since we don't send a response.
                        _connection!.Logger.LogDispatchException(request.Connection,
                                                                 request.Path,
                                                                 request.Operation,
                                                                 exception);
                    }
                    else
                    {
                        response = new OutgoingResponse(request, remoteException);
                    }
                }

                // Send the response if the stream is bidirectional.
                if (response != null)
                {
                    try
                    {
                        await stream.SendResponseFrameAsync(response).ConfigureAwait(false);
                    }
                    catch (RemoteException ex)
                    {
                        // Send the exception as the response instead of sending the response from the dispatch
                        // This can occur if the response exceeds the peer's incoming frame max size.
                        await stream.SendResponseFrameAsync(new OutgoingResponse(request, ex)).ConfigureAwait(false);
                    }
                }
            }
            catch (RpcStreamAbortedException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                _ = AbortAsync(ex);
            }
            finally
            {
                stream?.Release();
            }
        }

        /// <summary>Send the GoAway or CloseConnection frame to initiate the shutdown of the connection. Before
        /// sending the frame, ShutdownAsync first ensures that no new streams are accepted. After sending the frame,
        /// ShutdownAsync waits for the streams to complete, the connection closure from the peer or the close
        /// timeout to close the connection. If ShutdownAsync is canceled, dispatch in progress are canceled and a
        /// GoAwayCanceled frame is sent to the peer to cancel its dispatches as well. Shutdown cancellation can
        /// lead to a speedier shutdown if dispatches are cancelable.</summary>
        private async Task ShutdownAsync(Exception exception, CancellationToken cancel = default)
        {
            Task shutdownTask;
            lock (_mutex)
            {
                if (_state == ConnectionState.Active && !IsDatagram)
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
                await shutdownTask.IceWaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (Protocol == Protocol.Ice1)
                {
                    // Cancel dispatch if shutdown is canceled.
                    _connection?.CancelDispatch();
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
                Debug.Assert(_connection != null);

                using IDisposable? scope = _connection.StartScope(Server);
                TimeSpan now = Time.Elapsed;
                try
                {
                    // Shutdown the multi-stream connection to prevent new streams from being created. This is done
                    // before the yield to ensure consistency between the connection shutdown state and the connection
                    // closing State.
                    (long, long) lastIncomingStreamIds = _connection.Shutdown();

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
                        _connection.AbortOutgoingStreams(RpcStreamErrorCode.ConnectionShutdown);

                        // Wait for incoming streams to complete before sending the CloseConnetion frame. Ice1 doesn't
                        // support sending the largest request ID with the CloseConnection frame. When the peer
                        // receives the CloseConnection frame, it indicates that no more requests will be dispatch and
                        // the peer can therefore cancel remaining pending invocations (which can safely be retried).
                        await _connection.WaitForEmptyIncomingStreamsAsync(cancel).ConfigureAwait(false);
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

                    // Close the connection. This sends a close notification to the peer. The peer is supposed
                    // to close the connection upon getting this notification.
                    await _connection.CloseAsync(ConnectionErrorCode.Shutdown, cancel).ConfigureAwait(false);

                    // Wait for peer to close the connection.
                    try
                    {
                        await WaitForCloseAsync(cancel).ConfigureAwait(false);
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
                _connection!.CancelDispatch();
            }
        }

        private async Task WaitForCloseAsync(CancellationToken cancel)
        {
            // Wait for the peer to close the connection and return.
            while (State != ConnectionState.Closed)
            {
                await _acceptStreamTask.IceWaitAsync(cancel).ConfigureAwait(false);
            }
            throw new ConnectionClosedException();
        }

        private async Task WaitForEmptyStreamsAsync(CancellationToken cancel)
        {
            // Wait for all the streams to complete or an unexpected connection closure.
            Task waitForEmptyStreams = _connection!.WaitForEmptyStreamsAsync(cancel);
            if (!waitForEmptyStreams.IsCompleted)
            {
                Task task = await Task.WhenAny(waitForEmptyStreams, WaitForCloseAsync(cancel)).ConfigureAwait(false);
                if (task != waitForEmptyStreams)
                {
                    // The peer closed the connection before the streams are completed. If it gracefully closed the
                    // connection, we continue waiting for the streams to complete.
                    try
                    {
                        await task.ConfigureAwait(false);
                    }
                    catch (ConnectionClosedException)
                    {
                        await waitForEmptyStreams.ConfigureAwait(false);
                    }
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
                (long, long) lastIncomingStreamIds = _connection!.Shutdown();

                // Yield before continuing to ensure the code below isn't executed with the mutex locked.
                await Task.Yield();

                // Abort non-processed outgoing streams before closing the connection to ensure the invocation
                // will fail with a retryable exception.
                _connection.AbortOutgoingStreams(RpcStreamErrorCode.ConnectionShutdownByPeer, lastOutgoingStreamIds);

                try
                {
                    Debug.Assert(_options!.CloseTimeout != TimeSpan.Zero);
                    using var cancelCloseSource = new CancellationTokenSource(_options.CloseTimeout);
                    CancellationToken cancel = cancelCloseSource.Token;

                    if (Protocol != Protocol.Ice1)
                    {
                        if (closing)
                        {
                            // Send back a GoAway frame if we just switched to the closing state. If we were already
                            // in the closing state, it has already been sent.
                            await _controlStream!.SendGoAwayFrameAsync(lastIncomingStreamIds,
                                                                       exception.Message,
                                                                       cancel).ConfigureAwait(false);

                            // Wait for the GoAwayCanceled frame from the peer, if received this will cancel the
                            // dispatch.
                            _ = WaitForGoAwayCanceledAsync();
                        }

                        // Wait for all the streams to complete.
                        await WaitForEmptyStreamsAsync(cancel).ConfigureAwait(false);

                        // Wait for the connection closure.
                        try
                        {
                            await WaitForCloseAsync(cancel).ConfigureAwait(false);
                        }
                        catch (TransportException)
                        {
                            // Ignore
                        }
                    }

                    // Abort the connection once all the streams have completed.
                    await AbortAsync(exception).ConfigureAwait(false);
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

            async Task WaitForGoAwayCanceledAsync()
            {
                try
                {
                    // Wait to receive the GoAwayCanceled frame.
                    await _peerControlStream!.ReceiveGoAwayCanceledFrameAsync().ConfigureAwait(false);

                    // Cancel the dispatch if the peer canceled the shutdown.
                    _connection!.CancelDispatch();
                }
                catch
                {
                }
            }
        }
    }
}
