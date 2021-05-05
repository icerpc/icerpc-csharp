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
    /// <summary>The state of an Ice connection.</summary>
    public enum ConnectionState : byte
    {
        /// <summary>The connection is not initialized.</summary>
        NotInitialized,
        /// <summary>The connection is being initialized.</summary>
        Initializing,
        /// <summary>The connection is active and can send and receive messages.</summary>
        Active,
        /// <summary>The connection is being gracefully shutdown and waits for the peer to close its end of the
        /// connection.</summary>
        Closing,
        /// <summary>The connection is closed and eventually waits for potential dispatch to be finished before being
        /// destroyed.</summary>
        Closed
    }

    /// <summary>Represents a connection used to send and receive Ice frames.</summary>
    public class Connection : IAsyncDisposable
    {
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

        /// <summary>The features of this connection.</summary>
        public FeatureCollection Features { get; set; }

        /// <summary>Gets the connection idle timeout.</summary>
        public TimeSpan IdleTimeout
        {
            get
            {
                lock (_mutex)
                {
                    return _socket.IdleTimeout;
                }
            }
            set
            {
                if (value == TimeSpan.Zero)
                {
                    throw new ArgumentException("0 is not a valid value for IdleTimeout");
                }

                lock (_mutex)
                {
                    if (_state == ConnectionState.Active)
                    {
                        // Setting the IdleTimeout might throw if it's not supported by the underlying transport. For
                        // example with Slic, the idle timeout is negotiated when the connection is established, it
                        // can't be updated after.
                        _socket.IdleTimeout = value;

                        _timer?.Dispose();
                        _timer = null;

                        if (value != Timeout.InfiniteTimeSpan)
                        {
                            TimeSpan period = value / 2;
                            _timer = new Timer(value => Monitor(), null, period, period);
                        }
                    }
                }
            }
        }

        /// <summary><c>true</c> for datagram connections <c>false</c> otherwise.</summary>
        public bool IsDatagram => _socket.IsDatagram;

        /// <summary><c>true</c> for incoming connections <c>false</c> otherwise.</summary>
        public bool IsIncoming => Server != null;

        /// <summary><c>true</c> if the connection uses encryption <c>false</c> otherwise.</summary>
        public virtual bool IsSecure => Socket.IsSecure;

        /// <summary>Enables or disables the keep alive. When enabled, the connection is kept alive by sending ping
        /// frames at regular time intervals when the connection is idle.</summary>
        public bool KeepAlive { get; set; }

        /// <summary>The connection local endpoint.</summary>
        /// <exception name="InvalidOperationException">Throw if the local endpoint is not available. This can occur
        /// if the connection is a client connection which is not connected yet.</exception>
        public Endpoint LocalEndpoint => _socket.LocalEndpoint;

        /// <summary>The peer's incoming frame maximum size. This is only supported with ice2 connections. For
        /// ice1 connections, the value is always -1.</summary>
        public int PeerIncomingFrameMaxSize => Protocol == Protocol.Ice1 ? -1 : _socket.PeerIncomingFrameMaxSize!.Value;

        /// <summary>The protocol used by the connection.</summary>
        public Protocol Protocol => _socket.Protocol;

        /// <summary>The connection remote endpoint.</summary>
        /// <exception name="InvalidOperationException">Throw if the remote endpoint is not available.</exception>
        public Endpoint RemoteEndpoint => _socket.RemoteEndpoint;

        /// <summary>The server that created this incoming connection.</summary>
        public Server? Server { get; }

        /// <summary>The socket interface provides information on the socket used by the connection.</summary>
        public ISocket Socket => _socket.Socket;

        /// <summary>The socket transport.</summary>
        public Transport Transport => _socket.Transport;

        /// <summary>The socket transport name.</summary>
        public string TransportName => _socket.TransportName;
        internal int ClassGraphMaxDepth { get; }
        internal ILogger Logger => _socket.Logger;

        // Delegate used to remove the connection once it has been closed.
        internal Action<Connection>? Remove
        {
            set
            {
                lock (_mutex)
                {
                    // If the connection was closed before the delegate was set execute it immediately otherwise
                    // it will be called once the connection is closed.
                    if (_state == ConnectionState.Closed)
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
        private readonly ConnectionOptions _options;
        // The control stream is assigned on the connection initialization and is immutable once the connection
        // reaches the Active state.
        private SocketStream? _controlStream;
        private EventHandler? _closed;
        private readonly TimeSpan _closeTimeout;
        // The close task is assigned when GoAwayAsync or AbortAsync are called, it's protected with _mutex.
        private Task? _closeTask;
        private IDispatcher? _dispatcher;

        // The mutex protects mutable non-volatile data members and ensures the logic for some operations is
        // performed atomically.
        private readonly object _mutex = new();
        private SocketStream? _peerControlStream;

        private Action<Connection>? _remove;
        private readonly MultiStreamSocket _socket;
        private volatile ConnectionState _state; // The current state.
        private Timer? _timer;

        // TODO: remove for testing purpose only
        static public async Task<Connection> CreateAsync(Endpoint endpoint, Communicator communicator)
        {
            MultiStreamSocket socket = endpoint.CreateClientSocket(
                communicator.ConnectionOptions ?? OutgoingConnectionOptions.Default,
                communicator.Logger);
            var connection = new Connection(socket, communicator.ConnectionOptions ?? OutgoingConnectionOptions.Default);
            await connection.ConnectAsync(default).ConfigureAwait(false);
            return connection;
        }

        /// <summary>Aborts the connection.</summary>
        /// <param name="message">A description of the connection abortion reason.</param>
        public Task AbortAsync(string? message = null)
        {
            using IDisposable? scope = _socket.StartScope(Server);
            return AbortAsync(new ConnectionClosedException(message ?? "connection closed forcefully",
                                                            isClosedByPeer: false,
                                                            RetryPolicy.AfterDelay(TimeSpan.Zero)));
        }

        /// <summary>This event is raised when the connection is closed. If the subscriber needs more information about
        /// the closure, it can call Connection.ThrowException. The connection object is passed as the event sender
        /// argument.</summary>
        public event EventHandler? Closed
        {
            add
            {
                lock (_mutex)
                {
                    if (_state >= ConnectionState.Closed)
                    {
                        Task.Run(() => value?.Invoke(this, EventArgs.Empty));
                    }
                    _closed += value;
                }
            }
            remove => _closed -= value;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            _timer?.Dispose(); // AbortAsync calls dispose on the timer, but we do it again here to prevent a warning.
            return new(GoAwayAsync());
        }

        /// <summary>Gracefully closes the connection by sending a GoAway frame to the peer.</summary>
        /// <param name="message">The message transmitted to the peer with the GoAway frame.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public Task GoAwayAsync(string? message = null, CancellationToken cancel = default) =>
            GoAwayAsync(new ConnectionClosedException(message ?? "connection closed gracefully",
                                                      isClosedByPeer: false,
                                                      RetryPolicy.AfterDelay(TimeSpan.Zero)),
                        cancel);

        /// <summary>This event is raised when the connection receives a ping frame. The connection object is
        /// passed as the event sender argument.</summary>
        public event EventHandler? PingReceived
        {
            add => _socket.Ping += value;
            remove => _socket.Ping -= value;
        }

        /// <summary>Returns <c>true</c> if the connection is active. Outgoing streams can be created and incoming
        /// streams accepted when the connection is active. The connection is no longer considered active as soon
        /// as <see cref="GoAwayAsync(string?, CancellationToken)"/> is called to initiate a graceful connection
        /// closure.</summary>
        /// <return><c>true</c> if the connection is active, <c>false</c> if it's closing or closed.</return>
        public bool IsActive => _state == ConnectionState.Active;

        /// <summary>Sends an asynchronous ping frame.</summary>
        /// <param name="progress">Sent progress provider.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public async Task PingAsync(IProgress<bool>? progress = null, CancellationToken cancel = default)
        {
            await _socket.PingAsync(cancel).ConfigureAwait(false);
            progress?.Report(true);
        }

        /// <summary>Returns a description of the connection as human readable text, suitable for debugging.</summary>
        /// <returns>The description of the connection as human readable text.</returns>
        public override string? ToString() =>
            $"{Socket.GetType().FullName} ({Socket.Description}, IsIncoming={IsIncoming})";

        internal Connection(MultiStreamSocket socket, ConnectionOptions options, Server? server = null)
        {
            ClassGraphMaxDepth = options.ClassGraphMaxDepth;
            Features = options.Features;
            KeepAlive = options.KeepAlive;
            _closeTimeout = options.CloseTimeout;
            Server = server;
            _closeTimeout = options.CloseTimeout;
            _options = options;
            _socket = socket;
            _state = ConnectionState.NotInitialized;
        }

        internal async Task AcceptAsync(CancellationToken cancel)
        {
            if (_options is IncomingConnectionOptions options)
            {
                await _socket.AcceptAsync(options.AuthenticationOptions, cancel).ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException("can't accept a client connection");
            }

            lock (_mutex)
            {
                using IDisposable? scope = _socket.StartScope(Server);
                if (IsDatagram)
                {
                    _socket.Logger.LogStartReceivingDatagrams();
                }
                else
                {
                    _socket.Logger.LogConnectionAccepted();
                }

                _state = ConnectionState.Initializing;
            }

            // Perform protocol level initialization
            await InitializeAsync(cancel).ConfigureAwait(false);
        }

        internal async Task ConnectAsync(CancellationToken cancel)
        {
            if (_options is OutgoingConnectionOptions options)
            {
                // If the endpoint is secure, connect with the SSL client authentication options.
                SslClientAuthenticationOptions? authenticationOptions = null;
                if (_socket.RemoteEndpoint.IsSecure ?? true)
                {
                    authenticationOptions = options.AuthenticationOptions?.Clone() ?? new();
                    authenticationOptions.TargetHost ??= _socket.RemoteEndpoint.Host;
                    authenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol> {
                        new SslApplicationProtocol(Protocol.GetName())
                    };
                }
                await _socket.ConnectAsync(authenticationOptions, cancel).ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException("can't connect a server connection");
            }

            lock (_mutex)
            {
                using IDisposable? scope = _socket.StartScope(Server);
                if (IsDatagram)
                {
                    _socket.Logger.LogStartSendingDatagrams();
                }
                else
                {
                    _socket.Logger.LogConnectionEstablished();
                }

                _state = ConnectionState.Initializing;
            }

            // Perform protocol level initialization.
            await InitializeAsync(cancel).ConfigureAwait(false);
        }

        internal SocketStream CreateStream(bool bidirectional)
        {
            // Ensure the stream is created in the active state only, no new streams should be created if the
            // connection is closing or closed.
            lock (_mutex)
            {
                if (_state != ConnectionState.Active)
                {
                    throw new ConnectionClosedException(isClosedByPeer: false,
                                                        RetryPolicy.AfterDelay(TimeSpan.Zero));
                }
                return _socket.CreateStream(bidirectional);
            }
        }

        internal async Task GoAwayAsync(Exception exception, CancellationToken cancel = default)
        {
            using IDisposable? socketScope = _socket.StartScope(Server);
            try
            {
                Task goAwayTask;
                lock (_mutex)
                {
                    if (_state == ConnectionState.Active && !IsDatagram)
                    {
                        SetState(ConnectionState.Closing, exception);
                        _closeTask ??= PerformGoAwayAsync(exception);
                        Debug.Assert(_closeTask != null);
                    }
                    goAwayTask = _closeTask ?? AbortAsync(exception);
                }
                await goAwayTask.WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore if user cancellation token got canceled.
            }
            catch (Exception ex)
            {
                // PerformGoAwayAsync doesn't throw.
                Debug.Assert(false, $"unexpected exception {ex}");
            }

            async Task PerformGoAwayAsync(Exception exception)
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
                    Debug.Assert(_closeTimeout != TimeSpan.Zero);
                    using var source = new CancellationTokenSource(_closeTimeout);
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
                        if (_state == ConnectionState.Closed)
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

        internal async Task InitializeAsync(CancellationToken cancel)
        {
            using IDisposable? socketScope = _socket.StartScope(Server);
            try
            {
                // Initialize the transport.
                await _socket.InitializeAsync(cancel).ConfigureAwait(false);

                if (!IsDatagram)
                {
                    // Create the control stream and send the initialize frame
                    _controlStream = await _socket.SendInitializeFrameAsync(cancel).ConfigureAwait(false);

                    // Wait for the peer control stream to be accepted and read the initialize frame
                    _peerControlStream = await _socket.ReceiveInitializeFrameAsync(cancel).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _ = AbortAsync(ex);
                throw;
            }

            lock (_mutex)
            {
                if (_state >= ConnectionState.Closed)
                {
                    // This can occur if the communicator or server is disposed while the connection
                    // initializes.
                    throw new ConnectionClosedException(isClosedByPeer: false,
                                                        RetryPolicy.AfterDelay(TimeSpan.Zero));
                }
                SetState(ConnectionState.Active);
            }

            // Start a task to wait for the GoAway frame on the peer's control stream.
            if (!IsDatagram)
            {
                _ = Task.Run(async () => await WaitForGoAwayAsync().ConfigureAwait(false), default);
            }

            // Start the asynchronous AcceptStream operation from the thread pool to prevent reading
            // synchronously new frames from this thread.
            _acceptStreamTask = Task.Run(async () => await AcceptStreamAsync().ConfigureAwait(false), default);
        }

        internal void Monitor()
        {
            lock (_mutex)
            {
                if (_state != ConnectionState.Active)
                {
                    return;
                }

                TimeSpan idleTime = Time.Elapsed - _socket.LastActivity;

                if (idleTime > IdleTimeout / 4 && (KeepAlive || _socket.IncomingStreamCount > 0))
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
                else if (idleTime > IdleTimeout)
                {
                    if (_socket.OutgoingStreamCount > 0)
                    {
                        // Close the connection if we didn't receive a heartbeat or if read/write didn't update the
                        // ACM activity in the last period.
                        _ = AbortAsync(new ConnectionClosedException("connection timed out",
                                                                     isClosedByPeer: false,
                                                                     RetryPolicy.AfterDelay(TimeSpan.Zero)));
                    }
                    else
                    {
                        // The connection is idle, close it.
                        _ = GoAwayAsync(new ConnectionClosedException("connection idle",
                                                                      isClosedByPeer: false,
                                                                      RetryPolicy.AfterDelay(TimeSpan.Zero)));
                    }
                }
            }
        }

        private async Task AbortAsync(Exception exception)
        {
            lock (_mutex)
            {
                if (_state < ConnectionState.Closed)
                {
                    if (_state == ConnectionState.NotInitialized)
                    {
                        // If the connection is not initialized yet, we print a trace to show that the connection got
                        // accepted before printing out the connection closed trace.
                        if (IsDatagram)
                        {
                            _socket.Logger.LogStartReceivingDatagrams();
                        }
                        else
                        {
                            _socket.Logger.LogConnectionAccepted();
                        }
                    }

                    if (IsDatagram && IsIncoming)
                    {
                        _socket.Logger.LogStopReceivingDatagrams();
                    }
                    else
                    {
                        // Trace the cause of unexpected connection closures
                        if (exception is ConnectionClosedException closedException)
                        {
                            _socket.Logger.LogConnectionClosed(exception.Message, closedException.IsClosedByPeer);
                        }
                        else if (_state == ConnectionState.Closing)
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

                    SetState(ConnectionState.Closed, exception);
                    _closeTask = PerformAbortAsync();
                }
            }

            await _closeTask!.ConfigureAwait(false);

            async Task PerformAbortAsync()
            {
                _socket.Abort(exception);

                // Dispose of the socket.
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

        internal IDisposable? StartScope() => _socket.StartScope();

        private async ValueTask AcceptStreamAsync()
        {
            SocketStream? stream = null;
            while (stream == null)
            {
                try
                {
                    // Accept a new stream.
                    stream = await _socket.AcceptStreamAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (ConnectionClosedException) when (
                    (_state != ConnectionState.Closed && _peerControlStream!.ReceivedEndOfStream) ||
                    _state == ConnectionState.Closing)
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
                using IncomingRequest request =
                    await stream.ReceiveRequestFrameAsync(cancel).ConfigureAwait(false);
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
                _socket.Logger.LogReceivedRequest(request);

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
                    _socket.Logger.LogDispatchCanceledByClient(request);
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
                        _socket.Logger.LogDispatchException(request, ex);
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
                            _socket.Logger.LogDispatchException(request, ex);
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

        private void SetState(ConnectionState state, Exception? exception = null)
        {
            // If SetState() is called with an exception, then only closed and closing states are permissible.
            Debug.Assert((exception == null && state < ConnectionState.Closing) ||
                         (exception != null && state >= ConnectionState.Closing));

            Debug.Assert(_state < state); // Don't switch twice and only switch to a higher value state.

            if (state == ConnectionState.Active)
            {
                // Setup a timer to check for the connection idle time every IdleTimeout / 2 period. If the transport
                // doesn't support idle timeout (e.g.: the colocated transport), IdleTimeout will be infinite.
                if (_socket.IdleTimeout != Timeout.InfiniteTimeSpan)
                {
                    TimeSpan period = _socket.IdleTimeout / 2;
                    _timer = new Timer(value => Monitor(), null, period, period);
                }
            }

            _state = state;
        }

        private async Task WaitForGoAwayAsync()
        {
            try
            {
                // Wait to receive the GoAway frame on the control stream.
                ((long Bidirectional, long Unidirectional) lastStreamIds, string message) =
                    await _peerControlStream!.ReceiveGoAwayFrameAsync().ConfigureAwait(false);

                Task goAwayTask;
                lock (_mutex)
                {
                    var exception = new ConnectionClosedException(message,
                                                                  isClosedByPeer: true,
                                                                  RetryPolicy.AfterDelay(TimeSpan.Zero));
                    if (_state == ConnectionState.Active)
                    {
                        SetState(ConnectionState.Closing, exception);
                        goAwayTask = PerformGoAwayAsync(lastStreamIds, exception);
                    }
                    else if (_state == ConnectionState.Closing)
                    {
                        // We already initiated graceful connection closure. If the peer did as well, we can cancel
                        // incoming/outgoing streams.
                        goAwayTask = PerformGoAwayAsync(lastStreamIds, exception);
                    }
                    else
                    {
                        goAwayTask = _closeTask!;
                    }
                }

                await goAwayTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await AbortAsync(ex).ConfigureAwait(false);
            }

            async Task PerformGoAwayAsync((long Bidirectional, long Unidirectional) lastStreamIds, Exception exception)
            {
                // Abort non-processed outgoing streams and all incoming streams.
                _socket.AbortStreams(
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
