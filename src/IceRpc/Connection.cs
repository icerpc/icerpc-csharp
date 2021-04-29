// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
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
    public abstract class Connection : IAsyncDisposable
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

        /// <summary>Gets the endpoint from which the connection was created.</summary>
        /// <value>The endpoint from which the connection was created.</value>
        public Endpoint Endpoint { get; }

        /// <summary>Gets the connection idle timeout.</summary>
        public TimeSpan IdleTimeout
        {
            get
            {
                lock (_mutex)
                {
                    return Socket.IdleTimeout;
                }
            }
            set
            {
                lock (_mutex)
                {
                    if (_state == ConnectionState.Active)
                    {
                        // Setting the IdleTimeout might throw if it's not supported by the underlying transport. For
                        // example with Slic, the idle timeout is negotiated when the connection is established, it
                        // can't be updated after.
                        if (value == TimeSpan.Zero)
                        {
                            throw new InvalidConfigurationException("0 is not a valid value for IdleTimeout");
                        }
                        Socket.IdleTimeout = value;

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

        /// <summary><c>true</c> for incoming connections <c>false</c> otherwise.</summary>
        public bool IsIncoming { get; }

        /// <summary><c>true</c> if the connection uses encryption <c>false</c> otherwise.</summary>
        public virtual bool IsSecure => false;

        /// <summary>Enables or disables the keep alive. When enabled, the connection is kept alive by sending ping
        /// frames at regular time intervals when the connection is idle.</summary>
        public bool KeepAlive { get; set; }

        /// <summary>The peer's incoming frame maximum size. This is only supported with ice2 connections. For
        /// ice1 connections, the value is always -1.</summary>
        public int PeerIncomingFrameMaxSize => Protocol == Protocol.Ice1 ? -1 : Socket.PeerIncomingFrameMaxSize!.Value;

        /// <summary>The protocol used by the connection.</summary>
        public Protocol Protocol => Endpoint.Protocol;

        /// <summary>The server that created this incoming connection.</summary>
        public Server? Server { get; }

        internal CompressionLevel CompressionLevel { get; }
        internal int CompressionMinSize { get; }
        internal int ClassGraphMaxDepth { get; }

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

        // This property should be private protected, it's internal instead for testing purpose.
        internal MultiStreamSocket Socket { get; }
        // The accept stream task is assigned each time a new accept stream async operation is started.
        private volatile Task _acceptStreamTask = Task.CompletedTask;

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
        private Action<Connection>? _remove;
        private volatile ConnectionState _state; // The current state.
        private Timer? _timer;

        public static async Task<Connection> CreateAsync(
            Endpoint endpoint,
            Communicator communicator,
            OutgoingConnectionOptions? options = null,
            CancellationToken cancel = default)
        {
            // Perform connection establishment to the endpoint.
            Connection connection = await endpoint.ConnectAsync(
                options ?? OutgoingConnectionOptions.Default,
                communicator.Logger,
                cancel).ConfigureAwait(false);

            // Perform protocol level initialization.
            await connection.InitializeAsync(cancel).ConfigureAwait(false);

            return connection;
        }

        /// <summary>Aborts the connection.</summary>
        /// <param name="message">A description of the connection abortion reason.</param>
        public Task AbortAsync(string? message = null)
        {
            using IDisposable? scope = Socket.StartScope(Server);
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
            add => Socket.Ping += value;
            remove => Socket.Ping -= value;
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
            await Socket.PingAsync(cancel).ConfigureAwait(false);
            progress?.Report(true);
        }

        /// <summary>Returns a description of the connection as human readable text, suitable for logging or error
        /// messages.</summary>
        /// <returns>The description of the connection as human readable text.</returns>
        public override string ToString() => Socket.ToString()!;

        internal Connection(
            Endpoint endpoint,
            MultiStreamSocket socket,
            ConnectionOptions options,
            Server? server)
        {
            CompressionLevel = options.CompressionLevel;
            CompressionMinSize = options.CompressionMinSize;
            ClassGraphMaxDepth = options.ClassGraphMaxDepth;
            Socket = socket;
            Endpoint = endpoint;
            KeepAlive = options.KeepAlive;
            IsIncoming = server != null;
            _closeTimeout = options.CloseTimeout;
            Server = server;
            _state = ConnectionState.NotInitialized;
        }

        internal async Task AcceptAsync(SslServerAuthenticationOptions? authentication, CancellationToken cancel)
        {
            await Socket.AcceptAsync(authentication, cancel).ConfigureAwait(false);

            lock (_mutex)
            {
                using IDisposable? scope = Socket.StartScope(Server);
                if (Endpoint.IsDatagram)
                {
                    Socket.Logger.LogStartReceivingDatagrams();
                }
                else
                {
                    Socket.Logger.LogConnectionAccepted();
                }

                _state = ConnectionState.Initializing;
            }
        }

        internal async Task ConnectAsync(SslClientAuthenticationOptions? authentication, CancellationToken cancel)
        {
            await Socket.ConnectAsync(authentication, cancel).ConfigureAwait(false);

            lock (_mutex)
            {
                using IDisposable? scope = Socket.StartScope(Server);
                if (Endpoint.IsDatagram)
                {
                    Socket.Logger.LogStartSendingDatagrams();
                }
                else
                {
                    Socket.Logger.LogConnectionEstablished();
                }

                _state = ConnectionState.Initializing;
            }
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
                return Socket.CreateStream(bidirectional);
            }
        }

        internal async Task GoAwayAsync(Exception exception, CancellationToken cancel = default)
        {
            using IDisposable? socketScope = Socket.StartScope(Server);
            try
            {
                Task goAwayTask;
                lock (_mutex)
                {
                    if (_state == ConnectionState.Active && !Endpoint.IsDatagram)
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
                (long, long) lastIncomingStreamIds = Socket.AbortStreams(exception, stream => !stream.IsIncoming);

                // With Ice1, we first wait for all incoming streams to complete before sending the GoAway frame.
                if (Endpoint.Protocol == Protocol.Ice1)
                {
                    await Socket.WaitForEmptyStreamsAsync().ConfigureAwait(false);
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
                        // We can't just wait for the accept stream task failure as the task can sometime succeeds
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
            using IDisposable? socketScope = Socket.StartScope(Server);
            try
            {
                // Initialize the transport.
                await Socket.InitializeAsync(cancel).ConfigureAwait(false);

                if (!Endpoint.IsDatagram)
                {
                    // Create the control stream and send the initialize frame
                    _controlStream = await Socket.SendInitializeFrameAsync(cancel).ConfigureAwait(false);

                    // Wait for the peer control stream to be accepted and read the initialize frame
                    SocketStream peerControlStream =
                        await Socket.ReceiveInitializeFrameAsync(cancel).ConfigureAwait(false);

                    // Setup a task to wait for the close frame on the peer's control stream.
                    _ = Task.Run(
                        async () => await WaitForGoAwayAsync(peerControlStream).ConfigureAwait(false),
                        default);
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

                // Start the asynchronous AcceptStream operation from the thread pool to prevent eventually reading
                // synchronously new frames from this thread.
                _acceptStreamTask = Task.Run(async () => await AcceptStreamAsync().ConfigureAwait(false), default);
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

                TimeSpan idleTime = Time.Elapsed - Socket.LastActivity;

                if (idleTime > IdleTimeout / 4 && (KeepAlive || Socket.IncomingStreamCount > 0))
                {
                    // We send a ping if there was no activity in the last (IdleTimeout / 4) period. Sending a ping
                    // sooner than really needed is safer to ensure that the receiver will receive the ping in
                    // time. Sending the ping if there was no activity in the last (IdleTimeout / 2) period isn't
                    // enough since Monitor is called only every (IdleTimeout / 2) period. We also send a ping if
                    // dispatch are in progress to notify the peer that we're still alive.
                    //
                    // Note that this doesn't imply that we are sending 4 heartbeats per timeout period because
                    // Monitor is still only called every (IdleTimeout / 2) period.
                    _ = Socket.PingAsync(CancellationToken.None);
                }
                else if (idleTime > IdleTimeout)
                {
                    if (Socket.OutgoingStreamCount > 0)
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
                        if (Endpoint.IsDatagram)
                        {
                            Socket.Logger.LogStartReceivingDatagrams();
                        }
                        else
                        {
                            Socket.Logger.LogConnectionAccepted();
                        }
                    }

                    if (Endpoint.IsDatagram && IsIncoming)
                    {
                        Socket.Logger.LogStopReceivingDatagrams();
                    }
                    else
                    {
                        // Trace the cause of unexpected connection closures
                        if (exception is ConnectionClosedException closedException)
                        {
                            Socket.Logger.LogConnectionClosed(exception.Message, closedException.IsClosedByPeer);
                        }
                        else if (_state == ConnectionState.Closing)
                        {
                            Socket.Logger.LogConnectionClosed(exception.Message, closedByPeer: false);
                        }
                        else if (exception.IsConnectionLost())
                        {
                            Socket.Logger.LogConnectionClosed("connection lost", closedByPeer: true);
                        }
                        else
                        {
                            Socket.Logger.LogConnectionClosed(exception.Message, closedByPeer: false, exception);
                        }
                    }

                    SetState(ConnectionState.Closed, exception);
                    _closeTask = PerformAbortAsync();
                }
            }
            await _closeTask!.ConfigureAwait(false);

            async Task PerformAbortAsync()
            {
                Socket.Abort(exception);

                // Dispose of the socket.
                Socket.Dispose();

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
                    Socket.Logger.LogConnectionEventHandlerException("close", ex);
                }

                // Remove the connection from its factory. This must be called without the connection's mutex locked
                // because the factory needs to acquire an internal mutex and the factory might call on the connection
                // with its internal mutex locked.
                _remove?.Invoke(this);
            }
        }

        private async ValueTask AcceptStreamAsync()
        {
            SocketStream? stream = null;
            while (stream == null)
            {
                try
                {
                    // Accept a new stream.
                    stream = await Socket.AcceptStreamAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception) when (_state >= ConnectionState.Closing)
                {
                    // Don't abort, just end the accept stream task. The code is waiting for the
                    // connection closure when graceful connection closure is in progress and we
                    // wait the connection to be aborted by the graceful connection closure code
                    // to report an appropriate exception.
                    throw;
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

            string operation = "";
            string path = "";
            bool dispatchStarted = false;
            DispatchEventSource eventSource = Server?.DispatchEventSource ?? DispatchEventSource.Log;
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
                activity = Server?.ActivitySource?.StartActivity("IceRpc.Dispatch", ActivityKind.Server);
                if (activity == null && (Socket.Logger.IsEnabled(LogLevel.Critical) || Activity.Current != null))
                {
                    activity = new Activity("IceRpc.Dispatch");
                    // TODO we should start the activity after restoring its context, we should update this once
                    // we move to CreateActivity in .NET 6
                    activity.Start();
                }

                if (activity != null)
                {
                    activity.AddTag("Operation", request.Operation);
                    activity.AddTag("Path", request.Path);
                    request.RestoreActivityContext(activity);
                }

                path = request.Path;
                operation = request.Operation;
                eventSource.RequestStart(path, operation);
                dispatchStarted = true;

                // It is important to start the activity above before logging in case the logger has been configured to
                // include the activity tracking options.
                Socket.Logger.LogReceivedRequest(request);

                OutgoingResponse? response;

                try
                {
                    response = await DispatchAsync(request, cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // No need to send the response if the dispatch is canceled by the client.
                    Debug.Assert(cancel.IsCancellationRequested);
                    eventSource.RequestCanceled(path, operation);
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
                    Socket.Logger.LogSentResponse(response);
                }
            }
            catch (Exception ex)
            {
                if (dispatchStarted)
                {
                    // We only call DispatchFailed the IncommingRequest was read and Log.DispatchStart was called
                    eventSource.RequestFailed(path, operation, ex);
                }
                _ = AbortAsync(ex);
            }
            finally
            {
                if (dispatchStarted)
                {
                    eventSource.RequestStop(path, operation);
                }
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
                    Socket.Logger.LogDispatchCanceledByClient(request);
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
                        Socket.Logger.LogDispatchException(request, ex);
                        return OutgoingResponse.WithVoidReturnValue(new Dispatch(request));
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
                            Socket.Logger.LogDispatchException(request, ex);
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
                if (Socket.IdleTimeout != Timeout.InfiniteTimeSpan)
                {
                    TimeSpan period = Socket.IdleTimeout / 2;
                    _timer = new Timer(value => Monitor(), null, period, period);
                }
            }

            _state = state;
        }

        private async Task WaitForGoAwayAsync(SocketStream peerControlStream)
        {
            try
            {
                // Wait to receive the GoAway frame on the control stream.
                ((long Bidirectional, long Unidirectional) lastStreamIds, string message) =
                    await peerControlStream.ReceiveGoAwayFrameAsync().ConfigureAwait(false);

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
                        if (_closeTask == null)
                        {
                            _closeTask = goAwayTask;
                        }
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
                Socket.AbortStreams(exception,
                                    stream => stream.IsIncoming ||
                                              stream.IsBidirectional ?
                                                  stream.Id > lastStreamIds.Bidirectional :
                                                  stream.Id > lastStreamIds.Unidirectional);

                // Wait for all the streams to complete.
                await Socket.WaitForEmptyStreamsAsync().ConfigureAwait(false);

                try
                {
                    // Close the transport
                    await Socket.CloseAsync(exception, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    // Abort the connection once all the streams have completed.
                    await AbortAsync(exception).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>Represents a connection to a colocated server.</summary>
    public class ColocConnection : Connection
    {
        internal ColocConnection(
            Endpoint endpoint,
            ColocSocket socket,
            ConnectionOptions options,
            Server? server)
            : base(endpoint, socket, options, server)
        {
        }
    }

    /// <summary>Represents a connection to an IP-endpoint.</summary>
    public abstract class IPConnection : Connection
    {
        private protected readonly MultiStreamOverSingleStreamSocket _socket;

        /// <summary>The socket local IP-endpoint or null if it is not available.</summary>
        public System.Net.IPEndPoint? LocalEndpoint
        {
            get
            {
                try
                {
                    return _socket.Underlying.Socket?.LocalEndPoint as System.Net.IPEndPoint;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>The socket remote IP-endpoint or null if it is not available.</summary>
        public System.Net.IPEndPoint? RemoteEndpoint
        {
            get
            {
                try
                {
                    return _socket.Underlying.Socket?.RemoteEndPoint as System.Net.IPEndPoint;
                }
                catch
                {
                    return null;
                }
            }
        }

        internal IPConnection(
            Endpoint endpoint,
            MultiStreamOverSingleStreamSocket socket,
            ConnectionOptions options,
            Server? server)
            : base(endpoint, socket, options, server) => _socket = socket;
    }

    /// <summary>Represents a connection to a TCP-endpoint.</summary>
    public class TcpConnection : IPConnection
    {
        /// <summary>Gets a Boolean value that indicates whether the certificate revocation list is checked during the
        /// certificate validation process.</summary>
        public bool CheckCertRevocationStatus => SslStream?.CheckCertRevocationStatus ?? false;
        /// <summary>Gets a Boolean value that indicates whether this SslStream uses data encryption.</summary>
        public bool IsEncrypted => SslStream?.IsEncrypted ?? false;
        /// <summary>Gets a Boolean value that indicates whether both server and client have been authenticated.
        /// </summary>
        public bool IsMutuallyAuthenticated => SslStream?.IsMutuallyAuthenticated ?? false;
        /// <inheritdoc/>
        public override bool IsSecure => SslStream != null;
        /// <summary>Gets a Boolean value that indicates whether the data sent using this stream is signed.</summary>
        public bool IsSigned => SslStream?.IsSigned ?? false;

        /// <summary>Gets the certificate used to authenticate the local endpoint or null if no certificate was
        /// supplied.</summary>
        public X509Certificate? LocalCertificate => SslStream?.LocalCertificate;

        /// <summary>The negotiated application protocol in TLS handshake.</summary>
        public SslApplicationProtocol? NegotiatedApplicationProtocol => SslStream?.NegotiatedApplicationProtocol;

        /// <summary>Gets the cipher suite which was negotiated for this connection.</summary>
        public TlsCipherSuite? NegotiatedCipherSuite => SslStream?.NegotiatedCipherSuite;
        /// <summary>Gets the certificate used to authenticate the remote endpoint or null if no certificate was
        /// supplied.</summary>
        public X509Certificate? RemoteCertificate => SslStream?.RemoteCertificate;

        /// <summary>Gets a value that indicates the security protocol used to authenticate this connection or
        /// null if the connection is not secure.</summary>
        public SslProtocols? SslProtocol => SslStream?.SslProtocol;

        private SslStream? SslStream => _socket.Underlying.SslStream;

        internal TcpConnection(
            Endpoint endpoint,
            MultiStreamOverSingleStreamSocket socket,
            ConnectionOptions options,
            Server? server)
            : base(endpoint, socket, options, server)
        {
        }
    }

    /// <summary>Represents a connection to a UDP-endpoint.</summary>
    public class UdpConnection : IPConnection
    {
        /// <summary>The multicast IP-endpoint for a multicast connection otherwise null.</summary>
        public System.Net.IPEndPoint? MulticastEndpoint => _udpSocket.MulticastAddress;

        private readonly UdpSocket _udpSocket;

        internal UdpConnection(
            Endpoint endpoint,
            MultiStreamOverSingleStreamSocket socket,
            ConnectionOptions options,
            Server? server)
            : base(endpoint, socket, options, server) =>
            _udpSocket = (UdpSocket)_socket.Underlying;
    }

    /// <summary>Represents a connection to a WS-endpoint.</summary>
    public class WSConnection : TcpConnection
    {
        /// <summary>The HTTP headers in the WebSocket upgrade request.</summary>
        public IReadOnlyDictionary<string, string> Headers => _wsSocket.Headers;

        private readonly WSSocket _wsSocket;

        internal WSConnection(
            Endpoint endpoint,
            MultiStreamOverSingleStreamSocket socket,
            ConnectionOptions options,
            Server? server)
            : base(endpoint, socket, options, server) =>
            _wsSocket = (WSSocket)_socket.Underlying;
    }
}
