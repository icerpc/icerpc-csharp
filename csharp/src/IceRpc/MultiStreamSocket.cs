// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A multi-stream socket represents the local end of a network connection and enables transmitting raw
    /// binary data over multiple independent streams. The data sent and received over these streams can either be
    /// transmitted using a datagram oriented transport such as Quic or a stream oriented transport such as TCP
    /// (data multiplexing is used to transmit the data from multiple concurrent streams over the same TCP socket).
    /// The Ice core relies on a multi-stream sockets to support the Ice protocol.
    /// </summary>
    public abstract class MultiStreamSocket : IDisposable
    {
        /// <summary>The endpoint from which the socket was created.</summary>
        public Endpoint Endpoint { get; }

        /// <summary>Gets or set the idle timeout.</summary>
        public abstract TimeSpan IdleTimeout { get; internal set; }

        /// <summary><c>true</c> for incoming sockets <c>false</c> otherwise. An incoming socket is created
        /// by a server-side acceptor while an outgoing socket is created from the endpoint by the client-side.
        /// </summary>
        public bool IsIncoming { get; }

        internal int IncomingFrameMaxSize { get; }
        internal TimeSpan LastActivity { get; private set; }
        // The stream ID of the last received response with the Ice1 protocol. Keeping track of this stream ID is
        // necessary to avoid a race condition with the GoAway frame which could be received and processed before
        // the response is delivered to the stream.
        internal long LastResponseStreamId { get; set; }
        internal ILogger Logger { get; }
        internal int? PeerIncomingFrameMaxSize { get; set; }
        internal event EventHandler? Ping;
        internal int IncomingStreamCount => Thread.VolatileRead(ref _incomingStreamCount);
        internal int OutgoingStreamCount => Thread.VolatileRead(ref _outgoingStreamCount);

        private int _incomingStreamCount;
        // The mutex provides thread-safety for the _streamsAborted and LastActivity data members.
        private readonly object _mutex = new();
        private int _outgoingStreamCount;
        private readonly ConcurrentDictionary<long, SocketStream> _streams = new();
        private bool _streamsAborted;
        private volatile TaskCompletionSource? _streamsEmptySource;

        /// <summary>Aborts the socket.</summary>
        public abstract void Abort();

        /// <summary>Accept a new incoming connection. This is called after the acceptor accepted a new socket
        /// to perform blocking socket level initialization (TLS handshake, etc).</summary>
        /// <param name="authenticationOptions">The SSL authentication options for secure sockets.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract ValueTask AcceptAsync(
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel);

        /// <summary>Accepts an incoming stream.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The accepted stream.</return>
        public abstract ValueTask<SocketStream> AcceptStreamAsync(
            CancellationToken cancel);

        /// <summary>Connects a new outgoing connection. This is called after the endpoint created a new socket
        /// to establish the connection and perform  blocking socket level initialization (TLS handshake, etc).
        /// </summary>
        /// <param name="authenticationOptions">The SSL authentication options for secure sockets.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract ValueTask ConnectAsync(
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel);

        /// <summary>Closes the socket.</summary>
        /// <param name="exception">The exception for which the socket is closed.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract ValueTask CloseAsync(Exception exception, CancellationToken cancel);

        /// <summary>Creates an outgoing stream. Depending on the transport implementation, the stream ID might not
        /// be immediately available after the stream creation. It will be available after the first successful send
        /// call on the stream.</summary>
        /// <param name="bidirectional"><c>True</c> to create a bidirectional stream, <c>false</c> otherwise.</param>
        /// <return>The outgoing stream.</return>
        public abstract SocketStream CreateStream(bool bidirectional);

        /// <summary>Releases the resources used by the socket.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Initializes the transport.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract ValueTask InitializeAsync(CancellationToken cancel);

        /// <summary>Sends a ping frame to defer the idle timeout.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract Task PingAsync(CancellationToken cancel);

        /// <summary>The MultiStreamSocket constructor.</summary>
        /// <param name="endpoint">The endpoint from which the socket was created.</param>
        /// <param name="logger">The transport logger.</param>
        /// <param name="incomingFrameMaxSize">The incoming frame max size.</param>
        /// <param name="isIncoming">True if the socket is a server-side socket.</param>
        protected MultiStreamSocket(Endpoint endpoint, ILogger logger, int incomingFrameMaxSize, bool isIncoming)
        {
            Endpoint = endpoint;
            IsIncoming = isIncoming;
            IncomingFrameMaxSize = incomingFrameMaxSize;
            LastActivity = Time.Elapsed;
            Logger = logger;
        }

        /// <summary>Releases the resources used by the socket.</summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only
        /// unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            // Release the remaining streams.
            foreach (SocketStream stream in _streams.Values)
            {
                try
                {
                    stream.Release();
                }
                catch (Exception ex)
                {
                    Debug.Assert(false, $"unexpected exception on Stream.TryRelease: {ex}");
                }
            }
        }

        /// <summary>Traces the given received amount of data. Transport implementations should call this method
        /// to trace the received data.</summary>
        /// <param name="size">The size in bytes of the received data.</param>
        protected void Received(int size)
        {
            lock (_mutex)
            {
                Debug.Assert(size > 0);
                LastActivity = Time.Elapsed;
            }

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogReceivedData(size, Endpoint.Transport);
            }
        }

        /// <summary>Notifies event handlers of the received ping. Transport implementations should call this method
        /// when a ping is received.</summary>
        protected void ReceivedPing()
        {
            // Capture the event handler which can be modified anytime by the user code.
            EventHandler? callback = Ping;
            if (callback != null)
            {
                Task.Run(() =>
                {
                    try
                    {
                        callback.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        if (Logger.IsEnabled(LogLevel.Error))
                        {
                            Logger.LogPingEventHandlerException(ex);
                        }
                    }
                });
            }
        }

        /// <summary>Traces the given sent amount of data. Transport implementations should call this method to
        /// trace the data sent.</summary>
        /// <param name="size">The size in bytes of the data sent.</param>
        protected void Sent(int size)
        {
            lock (_mutex)
            {
                Debug.Assert(size > 0);
                LastActivity = Time.Elapsed;
            }

            if (size > 0 && Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogSentData(size, Endpoint.Transport);
            }
        }

        /// <summary>Try to get a stream with the given ID. Transport implementations can use this method to lookup
        /// an existing stream.</summary>
        /// <param name="streamId">The stream ID.</param>
        /// <param name="value">If found, value is assigned to the stream value, null otherwise.</param>
        /// <return>True if the stream was found and value contains a non-null value, False otherwise.</return>
        protected bool TryGetStream<T>(long streamId, [NotNullWhen(returnValue: true)] out T? value)
            where T : SocketStream
        {
            if (_streams.TryGetValue(streamId, out SocketStream? stream))
            {
                value = (T)stream;
                return true;
            }
            value = null;
            return false;
        }

        internal void Abort(Exception exception)
        {
            // Abort the transport.
            Abort();

            // Consider the abort as graceful if the streams were already aborted.
            bool graceful;
            lock (_mutex)
            {
                graceful = _streamsAborted;
            }

            // Abort the streams if not already done. It's important to call this again even if has already been
            // called previously by graceful connection closure. Not all the streams might have been aborted and
            // at this point we want to make sure all the streams are aborted.
            AbortStreams(exception);

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                // Trace the cause of unexpected connection closures
                if (!graceful && !(exception is ConnectionClosedException || exception is ObjectDisposedException))
                {
                    Logger.LogConnectionClosed(Endpoint.Transport, exception);
                }
                else
                {
                    Logger.LogConnectionClosed(Endpoint.Transport);
                }
            }
        }

        internal virtual (long Bidirectional, long Unidirectional) AbortStreams(
            Exception exception,
            Func<SocketStream, bool>? predicate = null)
        {
            lock (_mutex)
            {
                // Set the _streamsAborted flag to prevent addition of new streams by AddStream. No more streams
                // will be added to _streams once this flag is true.
                _streamsAborted = true;
            }

            // Cancel the streams based on the given predicate. Control streams are not canceled since they are
            // still needed for sending and receiving GoAway frames.
            long largestBidirectionalStreamId = 0;
            long largestUnidirectionalStreamId = 0;
            foreach (SocketStream stream in _streams.Values)
            {
                if (!stream.IsControl && (predicate?.Invoke(stream) ?? true))
                {
                    stream.Abort(exception);
                }
                else if (stream.IsBidirectional)
                {
                    if (stream.Id > largestBidirectionalStreamId)
                    {
                        largestBidirectionalStreamId = stream.Id;
                    }
                }
                else if (stream.Id > largestUnidirectionalStreamId)
                {
                    largestUnidirectionalStreamId = stream.Id;
                }
            }
            return (largestBidirectionalStreamId, largestUnidirectionalStreamId);
        }

        internal void AddStream(long id, SocketStream stream, bool control)
        {
            lock (_mutex)
            {
                // It's important to hold the mutex here to ensure the check for _streamsAborted and the stream
                // addition to the dictionary is atomic.
                if (_streamsAborted)
                {
                    throw new ConnectionClosedException(isClosedByPeer: false, RetryPolicy.AfterDelay(TimeSpan.Zero));
                }
                _streams[id] = stream;
            }

            if (!control)
            {
                Interlocked.Increment(ref stream.IsIncoming ? ref _incomingStreamCount : ref _outgoingStreamCount);
            }
        }

        internal virtual async ValueTask<SocketStream> ReceiveInitializeFrameAsync(CancellationToken cancel = default)
        {
            SocketStream stream = await AcceptStreamAsync(cancel).ConfigureAwait(false);
            Debug.Assert(stream.IsControl); // The first stream is always the control stream
            await stream.ReceiveInitializeFrameAsync(cancel).ConfigureAwait(false);
            return stream;
        }

        internal bool RemoveStream(long id)
        {
            if (_streams.TryRemove(id, out SocketStream? stream))
            {
                if (!stream.IsControl)
                {
                    Interlocked.Decrement(ref stream.IsIncoming ? ref _incomingStreamCount : ref _outgoingStreamCount);
                }
                CheckStreamsEmpty();
                return true;
            }
            else
            {
                return false;
            }
        }

        internal virtual async ValueTask<SocketStream> SendInitializeFrameAsync(CancellationToken cancel = default)
        {
            SocketStream stream = CreateStream(bidirectional: false);
            Debug.Assert(stream.IsControl); // The first stream is always the control stream
            await stream.SendInitializeFrameAsync(cancel).ConfigureAwait(false);
            return stream;
        }

        internal abstract IDisposable? StartScope();

        internal virtual async ValueTask WaitForEmptyStreamsAsync()
        {
            if (IncomingStreamCount > 0 || OutgoingStreamCount > 0)
            {
                // Create a task completion source to wait for the streams to complete.
                _streamsEmptySource ??= new TaskCompletionSource();
                CheckStreamsEmpty();
                await _streamsEmptySource.Task.ConfigureAwait(false);
            }
        }

        private void CheckStreamsEmpty()
        {
            if (IncomingStreamCount == 0 && OutgoingStreamCount == 0)
            {
                _streamsEmptySource?.TrySetResult();
            }
        }
    }
}
