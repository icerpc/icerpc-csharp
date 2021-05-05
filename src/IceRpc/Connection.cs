// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

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
        /// connection before to switch to the <c>Closed</c> state. The peer while close its end of the connection
        /// only once its dispatch complete.</summary>
        Closing,
        /// <summary>The connection is closed.</summary>
        Closed
    }

    /// <summary>Represents a connection used to send and receive Ice frames.</summary>
    public sealed class Connection : IAsyncDisposable
    {
        /// <summary>This event is raised when the connection is closed. If the subscriber needs more information about
        /// the closure, it can call Connection.ThrowException. The connection object is passed as the event sender
        /// argument.</summary>
        public event EventHandler? Closed
        {
            add
            {
                lock (_mutex)
                {
                    if (State >= ConnectionState.Closed)
                    {
                        Task.Run(() => value?.Invoke(this, EventArgs.Empty));
                    }
                    _closed += value;
                }
            }
            remove => _closed -= value;
        }

        /// <summary>Gets or sets the dispatcher that dispatches requests received by this connection. For incoming
        /// connections, set is an invalid operation and get returns the dispatcher of the server that created this
        /// connection. For outgoing connections, set can be called during configuration.</summary>
        /// <value>The dispatcher that dispatches requests received by this connection, or null if no dispatcher is
        /// set.</value>
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
                    throw new InvalidOperationException("cannot change the dispatcher of an incoming connection");
                }
            }
        }

        // /// <summary>The features of this connection.</summary>
        // public FeatureCollection Features => _options?.Features ?? throw new InvalidOperationException();

        /// <summary>Gets the connection idle timeout. With Ice2, the IdleTimeout is negotiated when the
        /// connection is established. The lowest IdleTimeout from either the client or server is used.</summary>
        public TimeSpan IdleTimeout => _socket?.IdleTimeout ?? _options?.IdleTimeout ?? TimeSpan.Zero;

        /// <summary>Returns <c>true</c> if the connection is active. Outgoing streams can be created and incoming
        /// streams accepted when the connection is active. The connection is no longer considered active as soon
        /// as <see cref="ShutdownAsync(string?, CancellationToken)"/> is called to initiate a graceful connection
        /// closure.</summary>
        /// <return><c>true</c> if the connection is in the <c>ConnectionState.Active</c> state, <c>false</c>
        /// otherwise.</return>
        public bool IsActive => State == ConnectionState.Active;

        /// <summary><c>true</c> for datagram connections <c>false</c> otherwise.</summary>
        public bool IsDatagram => (_localEndpoint ?? _remoteEndpoint)?.IsDatagram ?? false;

        /// <summary><c>true</c> for incoming connections <c>false</c> otherwise.</summary>
        public bool IsIncoming => Server != null;

        /// <summary><c>true</c> if the connection uses a secure transport, <c>false</c> otherwise.</summary>
        /// <exception name="InvalidOperationException">Thrown if the connection is not connected.</exception>
        public bool IsSecure => Socket.IsSecure;

        /// <summary>The connection local endpoint.</summary>
        /// <exception name="InvalidOperationException">Thrown if the local endpoint is not available.</exception>
        public Endpoint? LocalEndpoint => IsIncoming ? _localEndpoint : _socket?.LocalEndpoint;

        /// <summary>The logger factory to use for creating the connection logger.</summary>
        /// <exception name="InvalidOperationException">Thrown if the state of the connection is not
        /// <c>ConnectionState.NotEstablished</c>.</exception>
        public ILoggerFactory? LoggerFactory
        {
            get => _loggerFactory;
            set
            {
                if (State > ConnectionState.NotConnected)
                {
                    throw new InvalidOperationException(
                        $"cannot change the connection's logger factory after calling {nameof(ConnectAsync)}");
                }
                _loggerFactory = value;
            }
        }

        /// <summary>The connection options.</summary>
        /// <exception name="InvalidOperationException">Thrown if the state of the connection is not
        /// <c>ConnectionState.NotEstablished</c>.</exception>
        public ConnectionOptions? Options
        {
            get => _options?.Clone();
            set
            {
                if (State > ConnectionState.NotConnected)
                {
                    throw new InvalidOperationException(
                        $"cannot change the connection's options after calling {nameof(ConnectAsync)}");
                }
                if (value == null)
                {
                    throw new InvalidArgumentException($"{nameof(value)} can't be null");
                }
                _options = value.Clone();
            }
        }

        /// <summary>The peer's incoming frame maximum size. This is not supported with ice1 connections.</summary>
        /// <exception name="InvalidOperationException">Thrown if the connection is not connected.</exception>
        /// <exception name="NotSupportedException">Thrown if the connection is an ice1 connection.</exception>
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
                return _socket!.PeerIncomingFrameMaxSize!.Value;
            }
        }

        /// <summary>This event is raised when the connection receives a ping frame. The connection object is
        /// passed as the event sender argument.</summary>
        public event EventHandler? PingReceived;

        /// <summary>The protocol used by the connection.</summary>
        public Protocol Protocol => (_localEndpoint ?? _remoteEndpoint)?.Protocol ?? Protocol.Ice2;

        /// <summary>The connection remote endpoint.</summary>
        /// <exception name="InvalidOperationException">Thrown if the remote endpoint is not available or if setting
        /// the remote endpoint is not allowed (the connection is connected or it's an incoming connection).</exception>
        public Endpoint? RemoteEndpoint
        {
             get => IsIncoming ? _socket?.RemoteEndpoint : _remoteEndpoint;
             set
             {
                if (State > ConnectionState.NotConnected)
                {
                    throw new InvalidOperationException(
                        $"cannot change the connection's remote endpoint after calling {nameof(ConnectAsync)}");
                }
                _remoteEndpoint = value;
             }
        }

        /// <summary>The server that created this incoming connection.</summary>
        public Server? Server
        {
            get => _server;
            set
            {
                if (State > ConnectionState.NotConnected)
                {
                    throw new InvalidOperationException(
                        $"cannot change the connection's server after calling {nameof(ConnectAsync)}");
                }
                _server = value;
            }
        }

        /// <summary>The socket interface provides information on the socket used by the connection.</summary>
        public ISocket Socket =>
            _socket?.Socket ?? throw new InvalidOperationException("the connection is not connected");

        /// <summary>The state of the connection.</summary>
        public ConnectionState State
        {
            get => _state;

            private set
            {
                Debug.Assert(_state < value); // Don't switch twice and only switch to a higher value state.

                // Setup a timer to check for the connection idle time every IdleTimeout / 2 period. If the
                // transport doesn't support idle timeout (e.g.: the colocated transport), IdleTimeout will
                // be infinite.
                if (value == ConnectionState.Active && _socket!.IdleTimeout != Timeout.InfiniteTimeSpan)
                {
                    TimeSpan period = _socket.IdleTimeout / 2;
                    _timer = new Timer(value => Monitor(), null, period, period);
                }

                _state = value;
            }
        }

        /// <summary>The socket transport.</summary>
        /// <exception name="InvalidOperationException">Thrown if there's no endpoint set.</exception>
        public Transport Transport =>
            (_localEndpoint ?? _remoteEndpoint)?.Transport ??
            throw new InvalidOperationException(
                $"{nameof(Transport)} is not available because there's no endpoint set");

        /// <summary>The socket transport name.</summary>
        /// <exception name="InvalidOperationException">Thrown if there's no endpoint set.</exception>
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
        private bool _connected;
        // The control stream is assigned on the connection initialization and is immutable once the connection
        // reaches the Active state.
        private SocketStream? _controlStream;
        private EventHandler? _closed;
        // The close task is assigned when ShutdownAsync or AbortAsync are called, it's protected with _mutex.
        private Task? _closeTask;
        private IDispatcher? _dispatcher;
        private readonly Endpoint? _localEndpoint;
        private ILogger? _logger;
        private ILoggerFactory? _loggerFactory;
        // The mutex protects mutable non-volatile data members and ensures the logic for some operations is
        // performed atomically.
        private readonly object _mutex = new();
        private ConnectionOptions? _options;
        private SocketStream? _peerControlStream;
        private Endpoint? _remoteEndpoint;
        private Action<Connection>? _remove;
        private Server? _server;
        private MultiStreamSocket? _socket;
        private volatile ConnectionState _state = ConnectionState.NotConnected;
        private Timer? _timer;

        public Connection()
        {
        }

        /// <summary>Aborts the connection. This methods switches the connection state to <c>ConnectionState.Closed</c>
        /// If `Closed` events are registered, it waits for the events to be executed.
        /// </summary>
        /// <param name="message">A description of the connection abortion reason.</param>
        public Task AbortAsync(string? message = null)
        {
            if (_socket == null)
            {
                throw new InvalidOperationException("connection is not established");
            }
            using IDisposable? scope = _socket.StartScope(Server);
            return AbortAsync(
                new ConnectionClosedException(message ?? "connection closed forcefully", isClosedByPeer: false));
        }

        public Task ConnectAsync(CancellationToken cancel = default)
        {
            ValueTask connectTask = new();
            MultiStreamSocket socket;
            lock(_mutex)
            {
                if (State == ConnectionState.Closed)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{this}");
                }
                else if (State > ConnectionState.NotConnected)
                {
                    throw new InvalidOperationException($"connection is already connected");
                }

                _options ??= IsIncoming ? new IncomingConnectionOptions() : new OutgoingConnectionOptions();
                if (_options is OutgoingConnectionOptions outgoingOptions)
                {
                    if (IsIncoming)
                    {
                        throw new InvalidOperationException(
                            "invalid outgoing connection options for incoming connection");
                    }

                    if (_socket == null)
                    {
                        if (_remoteEndpoint == null)
                        {
                            throw new InvalidOperationException("outgoing connection has no remote endpoint set");
                        }
                        else if(_localEndpoint != null)
                        {
                            throw new InvalidOperationException("outgoing connection has local endpoint set");
                        }

                        _socket = _remoteEndpoint.CreateClientSocket(outgoingOptions, Logger);
                    }

                    // If the endpoint is secure, connect with the SSL client authentication options.
                    SslClientAuthenticationOptions? clientAuthenticationOptions = null;
                    if (_socket.RemoteEndpoint.IsSecure ?? true)
                    {
                        clientAuthenticationOptions = outgoingOptions.AuthenticationOptions?.Clone() ?? new();
                        clientAuthenticationOptions.TargetHost ??= _socket.RemoteEndpoint.Host;
                        clientAuthenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol> {
                            new SslApplicationProtocol(Protocol.GetName())
                        };
                    }

                    connectTask = _socket.ConnectAsync(clientAuthenticationOptions, cancel);
                }
                else if (_options is IncomingConnectionOptions incomingOptions)
                {
                    if (!IsIncoming)
                    {
                        throw new InvalidOperationException(
                            "invalid incoming connection options for outgoing connection");
                    }
                    else if (_socket == null)
                    {
                        throw new InvalidOperationException(
                            $"incoming connection can only be created by a {nameof(Server)}");
                    }

                    // If the endpoint is secure, accept with the SSL server authentication options.
                    SslServerAuthenticationOptions? serverAuthenticationOptions = null;
                    if (_socket.LocalEndpoint.IsSecure ?? true)
                    {
                        serverAuthenticationOptions = incomingOptions.AuthenticationOptions?.Clone() ?? new();
                        serverAuthenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol> {
                            new SslApplicationProtocol(Protocol.GetName())
                        };
                    }

                    connectTask = _socket.AcceptAsync(serverAuthenticationOptions, cancel);
                }

                Debug.Assert(_socket != null);
                State = ConnectionState.Connecting;
                socket = _socket;
            }

            return PerformConnectAsync();

            async Task PerformConnectAsync()
            {
                try
                {
                    // Wait for the connection to be connected or accepted.
                    await connectTask.ConfigureAwait(false);

                    // Start the scope only once the socket is connected/accepted to ensure that the .NET socket
                    // endpoints are available.
                    using IDisposable? scope = socket.StartScope(Server);
                    lock (_mutex)
                    {
                        if (State == ConnectionState.Closed)
                        {
                            // This can occur if the communicator or server is disposed while the connection is being
                            // initialized.
                            throw new ConnectionClosedException();
                        }

                        // Set _connected to true to ensure that if AbortAsync is called concurrently, AbortAsync will
                        // trace the correct message.
                        _connected = true;

                        Action logSuccess = (IsIncoming, IsDatagram) switch
                        {
                            (false, false) => _socket.Logger.LogConnectionEstablished,
                            (false, true) => _socket.Logger.LogStartSendingDatagrams,
                            (true, false) => _socket.Logger.LogConnectionAccepted,
                            (true, true) => _socket.Logger.LogStartReceivingDatagrams
                        };
                        logSuccess();
                    }

                    // Initialize the transport.
                    await socket.InitializeAsync(cancel).ConfigureAwait(false);

                    if (!IsDatagram)
                    {
                        // Create the control stream and send the protocol initialize frame
                        _controlStream = await socket.SendInitializeFrameAsync(cancel).ConfigureAwait(false);

                        // Wait for the peer control stream to be accepted and read the protocol initialize frame
                        _peerControlStream = await socket.ReceiveInitializeFrameAsync(cancel).ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    using IDisposable? scope = socket.StartScope(Server);
                    await AbortAsync(exception).ConfigureAwait(false);
                    throw;
                }

                lock (_mutex)
                {
                    if (State == ConnectionState.Closed)
                    {
                        // This can occur if the communicator or server is disposed while the connection is being
                        // initialized.
                        throw new ConnectionClosedException();
                    }

                    _socket.PingReceived = () =>
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
                    State = ConnectionState.Active;

                    using IDisposable? scope = socket.StartScope(Server);

                    // Start a task to wait for the GoAway frame on the peer's control stream.
                    if (!IsDatagram)
                    {
                        _ = Task.Run(async () => await WaitForShutdownAsync().ConfigureAwait(false), default);
                    }

                    // Start the asynchronous AcceptStream operation from the thread pool to prevent reading
                    // synchronously new frames from this thread.
                    _acceptStreamTask = Task.Run(async () => await AcceptStreamAsync().ConfigureAwait(false), default);
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            try
            {
                return new(ShutdownAsync());

            }
            finally
            {
                // These are disposed by AbortAsync but we do it again here to prevent a warning.
                _timer?.Dispose();
                _socket?.Dispose();
            }
        }

        /// <summary>Sends an asynchronous ping frame.</summary>
        /// <param name="progress">Sent progress provider.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public async Task PingAsync(IProgress<bool>? progress = null, CancellationToken cancel = default)
        {
            if (_socket == null)
            {
                throw new InvalidOperationException("connection is not established");
            }
            await _socket.PingAsync(cancel).ConfigureAwait(false);
            progress?.Report(true);
        }

        /// <summary>Shuts down gracefully the connection by sending a GoAway frame to the peer.</summary>
        /// <param name="message">The message transmitted to the peer with the GoAway frame.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public Task ShutdownAsync(string? message = null, CancellationToken cancel = default) =>
            ShutdownAsync(
                new ConnectionClosedException(message ?? "connection closed gracefully", isClosedByPeer: false),
                cancel);

        /// <summary>Returns a description of the connection as human readable text, suitable for debugging.</summary>
        /// <returns>The description of the connection as human readable text.</returns>
        public override string? ToString() => Socket == null ? "" :
            $"{Socket.GetType().FullName} ({Socket.Description}, IsIncoming={IsIncoming})";

        /// <summary>Constructs an incoming connection from an accepted socket.</summary>
        internal Connection(MultiStreamSocket socket, Server server)
        {
            _socket = socket;
            _localEndpoint = socket.LocalEndpoint!;

            Options = server.ConnectionOptions;
            Server = server;
            Logger = server.Logger;
        }

        internal SocketStream CreateStream(bool bidirectional)
        {
            // Ensure the stream is created in the active state only, no new streams should be created if the
            // connection is closing or closed.
            lock (_mutex)
            {
                if (State != ConnectionState.Active)
                {
                    throw new ConnectionClosedException();
                }
                return _socket!.CreateStream(bidirectional);
            }
        }

        internal void Monitor()
        {
            lock (_mutex)
            {
                if (State != ConnectionState.Active)
                {
                    return;
                }
                Debug.Assert(_socket != null);

                TimeSpan idleTime = Time.Elapsed - _socket!.LastActivity;

                if (idleTime > _socket.IdleTimeout / 4 && (_options!.KeepAlive || _socket.IncomingStreamCount > 0))
                {
                    // We send a ping if there was no activity in the last (IdleTimeout / 4) period. Sending a ping
                    // sooner than really needed is safer to ensure that the receiver will receive the ping in
                    // time. Sending the ping if there was no activity in the last (IdleTimeout / 2) period isn't
                    // enough since Monitor is called only every (IdleTimeout / 2) period. We also send a ping if
                    // dispatch are in progress to notify the peer that we're still alive.
                    //
                    // Note that this doesn't imply that we are sending 4 heartbeats per timeout period because
                    // Monitor is still only called every (IdleTimeout / 2) period.
                    _ = _socket.PingAsync(CancellationToken.None);
                }
                else if (idleTime > _socket.IdleTimeout)
                {
                    if (_socket.OutgoingStreamCount > 0)
                    {
                        // Close the connection if we didn't receive a heartbeat or if read/write didn't update the
                        // ACM activity in the last period.
                        _ = AbortAsync(new ConnectionClosedException("connection timed out", isClosedByPeer: false));
                    }
                    else
                    {
                        // The connection is idle, close it.
                        _ = ShutdownAsync(new ConnectionClosedException("connection idle", isClosedByPeer: false));
                    }
                }
            }
        }

        private async Task AbortAsync(Exception exception)
        {
            lock (_mutex)
            {
                if (_socket != null && State < ConnectionState.Closed)
                {
                    if (State == ConnectionState.Connecting && !_connected)
                    {
                        // If the connection is connecting but not active yet, we print a trace to show that
                        // the connection got connected or accepted before printing out the connection closed
                        // trace.
                        Action<Exception> logFailure = (IsIncoming, IsDatagram) switch
                        {
                            (false, false) => _socket.Logger.LogConnectionConnectFailed,
                            (false, true) => _socket.Logger.LogStartSendingDatagramsFailed,
                            (true, false) => _socket.Logger.LogConnectionAcceptFailed,
                            (true, true) => _socket.Logger.LogStartReceivingDatagramsFailed
                        };
                        logFailure(exception);
                    }
                    else if (State > ConnectionState.Connecting || _connected)
                    {
                        if (IsDatagram && IsIncoming)
                        {
                            _socket.Logger.LogStopReceivingDatagrams();
                        }
                        else if (exception is ConnectionClosedException closedException)
                        {
                            _socket.Logger.LogConnectionClosed(exception.Message, closedException.IsClosedByPeer);
                        }
                        else if (State == ConnectionState.Closing)
                        {
                            _socket.Logger.LogConnectionClosed(exception.Message, closedByPeer: false);
                        }
                        else if (exception.IsConnectionLost())
                        {
                            _socket.Logger.LogConnectionClosed("connection lost", closedByPeer: true);
                        }
                        else
                        {
                            _socket.Logger.LogConnectionClosed(exception.Message, closedByPeer: false, exception);
                        }
                    }

                    State = ConnectionState.Closed;
                    _closeTask = PerformAbortAsync();
                }
            }

            await _closeTask!.ConfigureAwait(false);

            async Task PerformAbortAsync()
            {
                _socket.Abort(exception);

                // Dispose the disposable resources associated with the connection.
                _socket.Dispose();
                _timer?.Dispose();

                // Yield to ensure the code below is executed without the mutex locked (PerformAbortAsync is called
                // with the mutex locked).
                await Task.Yield();

                // Raise the Closed event, this will call user code so we shouldn't hold the mutex.
                try
                {
                    _closed?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    _socket.Logger.LogConnectionEventHandlerException("close", ex);
                }

                // Remove the connection from its factory. This must be called without the connection's mutex locked
                // because the factory needs to acquire an internal mutex and the factory might call on the connection
                // with its internal mutex locked.
                _remove?.Invoke(this);
            }
        }

        internal IDisposable? StartScope() => _socket!.StartScope();

        private async ValueTask AcceptStreamAsync()
        {
            SocketStream? stream = null;
            while (stream == null)
            {
                try
                {
                    // Accept a new stream.
                    stream = await _socket!.AcceptStreamAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (ConnectionClosedException) when (
                    (State != ConnectionState.Closed && _peerControlStream!.ReceivedEndOfStream) ||
                    State == ConnectionState.Closing)
                {
                    // Don't abort the connection if the connection is being gracefully closed (either the peer
                    // control stream is done which indicates the reception of the GoAway frame or the connection
                    // is in the closing state). We just ignore the failure to accept the new stream until the
                    // connection is closed (which is indicated by a ConnectionLostException).
                }
                catch (Exception ex)
                {
                    _ = AbortAsync(ex);
                    throw;
                }
            }

            // Start a new accept stream task to accept another stream.
            _acceptStreamTask = Task.Run(() => AcceptStreamAsync().AsTask());

            using IDisposable? streamScope = stream.StartScope();
            Activity? activity = null;

            Debug.Assert(stream != null);
            try
            {
                using var cancelSource = new CancellationTokenSource();
                CancellationToken cancel = cancelSource.Token;
                if (stream.IsBidirectional)
                {
                    // Be notified if the peer resets the stream to cancel the dispatch.
                    //
                    // The error code is ignored here since we can't provide it to the CancellationTokenSource. We
                    // could consider setting the error code into Current to allow the user to figure out the
                    // reason of the stream reset.
                    stream.Reset += (long errorCode) => cancelSource.Cancel();
                }

                // Receives the request frame from the stream
                using IncomingRequest request = await stream.ReceiveRequestFrameAsync(cancel).ConfigureAwait(false);
                request.Connection = this;
                request.StreamId = stream.Id;

                // TODO Use CreateActivity from ActivitySource once we move to .NET 6, to avoid starting the activity
                // before we restore its context.
                activity = Server?.ActivitySource?.StartActivity($"{request.Path}/{request.Operation}",
                                                                 ActivityKind.Server);
                if (activity == null && (_socket.Logger.IsEnabled(LogLevel.Critical) || Activity.Current != null))
                {
                    activity = new Activity($"{request.Path}/{request.Operation}");
                    // TODO we should start the activity after restoring its context, we should update this once
                    // we move to CreateActivity in .NET 6
                    activity.Start();
                }

                if (activity != null)
                {
                    activity.AddTag("rpc.system", "icerpc");
                    activity.AddTag("rpc.service", request.Path);
                    activity.AddTag("rpc.method", request.Operation);
                    // TODO add additional attributes
                    // https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/semantic_conventions/rpc.md#common-remote-procedure-call-conventions
                    request.RestoreActivityContext(activity);
                }

                // It is important to start the activity above before logging in case the logger has been configured to
                // include the activity tracking options.
                _socket!.Logger.LogReceivedRequest(request);

                OutgoingResponse? response;

                try
                {
                    response = await DispatchAsync(request, cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // No need to send the response if the dispatch is canceled by the client.
                    Debug.Assert(cancel.IsCancellationRequested);
                    return;
                }

                if (stream.IsBidirectional)
                {
                    Debug.Assert(response != null);
                    try
                    {
                        // Send the response over the stream
                        await stream.SendResponseFrameAsync(response, cancel).ConfigureAwait(false);
                    }
                    catch (RemoteException ex)
                    {
                        // Send the exception as the response instead of sending the response from the dispatch
                        // if sending raises a remote exception.
                        response = new OutgoingResponse(request, ex);
                        await stream.SendResponseFrameAsync(response, cancel).ConfigureAwait(false);
                    }
                    _socket.Logger.LogSentResponse(response);
                }
            }
            catch (Exception ex)
            {
                _ = AbortAsync(ex);
            }
            finally
            {
                activity?.Stop();
                stream?.Release();
            }
        }

        /// <summary>Dispatches a request by calling <see cref="IDispatcher.DispatchAsync"/> on the configured
        /// <see cref="Dispatcher"/>. If <c>DispatchAsync</c> throws a <see cref="RemoteException"/> with
        /// <see cref="RemoteException.ConvertToUnhandled"/> set to true, this method converts this exception into an
        /// <see cref="UnhandledException"/> response. If <see cref="Dispatcher"/> is null, this method returns a
        /// <see cref="ServiceNotFoundException"/> response.</summary>
        /// <param name="request">The request being dispatched.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task that provides the <see cref="OutgoingResponse"/> for the request.</returns>
        private async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            if (Dispatcher is IDispatcher dispatcher)
            {
                // cancel is canceled when the client cancels the call (resets the stream). We construct a separate
                // source/token that combines cancel and the server's own cancel dispatch token when dispatching to
                // a server.
                using CancellationTokenSource? combinedSource = request.Connection.Server != null ?
                    CancellationTokenSource.CreateLinkedTokenSource(cancel, request.Connection.Server.CancelDispatch) : null;

                try
                {
                    OutgoingResponse response =
                        await dispatcher.DispatchAsync(request, combinedSource?.Token ?? cancel).ConfigureAwait(false);

                    cancel.ThrowIfCancellationRequested();
                    return response;
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                    // The client requested cancellation, we log it and let it propagate.
                    _socket!.Logger.LogDispatchCanceledByClient(request);
                    throw;
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException &&
                        request.Connection.Server is Server server &&
                        server.CancelDispatch.IsCancellationRequested)
                    {
                        // Replace exception
                        ex = new ServerException("dispatch canceled by server shutdown");
                    }
                    // else it's another OperationCanceledException that the implementation should have caught, and it
                    // will become an UnhandledException below.

                    if (request.IsOneway)
                    {
                        // We log this exception, since otherwise it would be lost.
                        _socket!.Logger.LogDispatchException(request, ex);
                        return OutgoingResponse.WithVoidReturnValue(request);
                    }
                    else
                    {
                        RemoteException actualEx;
                        if (ex is RemoteException remoteEx && !remoteEx.ConvertToUnhandled)
                        {
                            actualEx = remoteEx;
                        }
                        else
                        {
                            actualEx = new UnhandledException(ex);

                            // We log the "source" exception as UnhandledException may not include all details.
                            _socket!.Logger.LogDispatchException(request, ex);
                        }
                        return new OutgoingResponse(request, actualEx);
                    }
                }
            }
            else
            {
                return new OutgoingResponse(request, new ServiceNotFoundException(RetryPolicy.OtherReplica));
            }
        }

        private async Task ShutdownAsync(Exception exception, CancellationToken cancel = default)
        {
            if (_socket == null)
            {
                throw new InvalidOperationException("connection is not established");
            }

            using IDisposable? socketScope = _socket.StartScope(Server);
            try
            {
                Task shutdownTask;
                lock (_mutex)
                {
                    if (State == ConnectionState.Active && !IsDatagram)
                    {
                        State = ConnectionState.Closing;
                        _closeTask ??= PerformShutdownAsync(exception);
                    }
                    shutdownTask = _closeTask ?? AbortAsync(exception);
                }
                await shutdownTask.WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore if user cancellation token got canceled.
            }
            catch (Exception ex)
            {
                // PerformShutdownAsync doesn't throw.
                Debug.Assert(false, $"unexpected exception {ex}");
            }

            async Task PerformShutdownAsync(Exception exception)
            {
                // Abort outgoing streams and get the largest incoming stream IDs. With Ice2, we don't wait for
                // the incoming streams to complete before sending the GoAway frame but instead provide the ID
                // of the latest incoming stream IDs to the peer. The peer will close the connection only once
                // the streams with IDs inferior or equal to the largest stream IDs are complete.
                (long, long) lastIncomingStreamIds = _socket.AbortStreams(exception, stream => !stream.IsIncoming);

                // With Ice1, we first wait for all incoming streams to complete before sending the GoAway frame.
                if (Protocol == Protocol.Ice1)
                {
                    await _socket.WaitForEmptyStreamsAsync().ConfigureAwait(false);
                }

                try
                {
                    Debug.Assert(_options!.CloseTimeout != TimeSpan.Zero);
                    using var source = new CancellationTokenSource(_options.CloseTimeout);
                    CancellationToken cancel = source.Token;

                    // Write the close frame
                    await _controlStream!.SendGoAwayFrameAsync(lastIncomingStreamIds,
                                                               exception.Message,
                                                               cancel).ConfigureAwait(false);

                    // Make sure to yield to release the mutex. It's important to not hold the mutex because the
                    // loop below waits for AbortAsync to be called and AbortAsync requires to lock the mutex.
                    await Task.Yield();

                    // Wait for the peer to close the connection.
                    while (true)
                    {
                        // We can't just wait for the accept stream task failure as the task can sometime succeed
                        // depending on the thread scheduling. So we also check for the state to ensure the loop
                        // eventually terminates once the peer connection is closed.
                        if (State == ConnectionState.Closed)
                        {
                            throw exception;
                        }
                        await _acceptStreamTask.WaitAsync(cancel).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    await AbortAsync(new TimeoutException("connection closure timed out")).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await AbortAsync(exception).ConfigureAwait(false);
                }
            }
        }

        private async Task WaitForShutdownAsync()
        {
            try
            {
                // Wait to receive the GoAway frame on the control stream.
                ((long Bidirectional, long Unidirectional) lastStreamIds, string message) =
                    await _peerControlStream!.ReceiveGoAwayFrameAsync().ConfigureAwait(false);

                Task shutdownTask;
                lock (_mutex)
                {
                    var exception = new ConnectionClosedException(message, isClosedByPeer: true);
                    if (State == ConnectionState.Active)
                    {
                        State = ConnectionState.Closing;
                        shutdownTask = PerformShutdownAsync(lastStreamIds, exception);
                    }
                    else if (State == ConnectionState.Closing)
                    {
                        // We already initiated graceful connection closure. If the peer did as well, we can cancel
                        // incoming/outgoing streams.
                        shutdownTask = PerformShutdownAsync(lastStreamIds, exception);
                    }
                    else
                    {
                        shutdownTask = _closeTask!;
                    }
                }

                await shutdownTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await AbortAsync(ex).ConfigureAwait(false);
            }

            async Task PerformShutdownAsync(
                (long Bidirectional, long Unidirectional) lastStreamIds,
                Exception exception)
            {
                // Abort non-processed outgoing streams and all incoming streams.
                _socket!.AbortStreams(
                    exception,
                    stream => stream.IsIncoming ||
                                stream.IsBidirectional ?
                                    stream.Id > lastStreamIds.Bidirectional :
                                    stream.Id > lastStreamIds.Unidirectional);

                // Wait for all the streams to complete.
                await _socket.WaitForEmptyStreamsAsync().ConfigureAwait(false);

                try
                {
                    // Close the transport
                    await _socket.CloseAsync(exception, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    // Abort the connection once all the streams have completed.
                    await AbortAsync(exception).ConfigureAwait(false);
                }
            }
        }
    }
}
