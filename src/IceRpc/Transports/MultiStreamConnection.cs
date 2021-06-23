// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports
{
    /// <summary>A multi-stream connection represents a network connection that provides multiple independent streams of
    /// binary data.</summary>
    /// <seealso cref="RpcStream"/>
    public abstract class MultiStreamConnection : IDisposable
    {
        /// <summary>Gets or set the idle timeout.</summary>
        public abstract TimeSpan IdleTimeout { get; internal set; }

        /// <summary><c>true</c> for datagram connections <c>false</c> otherwise.</summary>
        public bool IsDatagram => _endpoint.IsDatagram;

        /// <summary><c>true</c> for server connections; otherwise, <c>false</c>. An server connection is created
        /// by a server-side listener while an client connection is created from the endpoint by the client-side.
        /// </summary>
        public bool IsIncoming { get; }

        /// <summary>The local endpoint. The endpoint may not be available until the connection is connected.
        /// </summary>
        public Endpoint LocalEndpoint
        {
            get => _localEndpoint ?? throw new InvalidOperationException("the connection is not connected");
            set => _localEndpoint = value;
        }

        /// <summary>The Ice protocol used by this connection.</summary>
        public Protocol Protocol => _endpoint.Protocol;

        /// <summary>The remote endpoint. This endpoint may not be available until the connection is accepted.
        /// </summary>
        public Endpoint RemoteEndpoint
        {
            get => _remoteEndpoint ?? throw new InvalidOperationException("the connection is not connected");
            set => _remoteEndpoint = value;
        }

        /// <summary>Returns information about this connection.</summary>
        public abstract ConnectionInformation ConnectionInformation { get; }

        /// <summary>The transport of this connection.</summary>
        public Transport Transport => _endpoint.Transport;

        /// <summary>The name of the transport.</summary>
        public string TransportName => _endpoint.TransportName;

        internal int IncomingFrameMaxSize { get; }
        internal int IncomingStreamCount
        {
            get
            {
                lock (_mutex)
                {
                    return _incomingStreamCount;
                }
            }
        }

        internal TimeSpan LastActivity { get; private set; }

        // The stream ID of the last received response with the Ice1 protocol. Keeping track of this stream ID is
        // necessary to avoid a race condition with the GoAway frame which could be received and processed before
        // the response is delivered to the stream.
        internal long LastResponseStreamId { get; set; }
        internal int OutgoingStreamCount
        {
            get
            {
                lock (_mutex)
                {
                    return _outgoingStreamCount;
                }
            }
        }

        internal int? PeerIncomingFrameMaxSize { get; set; }
        internal ILogger Logger { get; }
        internal Action? PingReceived;

        // The endpoint which created the connection. If it's a server connection, it's the local endpoint or the remote
        // endpoint otherwise.
        private readonly Endpoint _endpoint;
        private int _incomingStreamCount;
        private TaskCompletionSource? _incomingStreamsEmptySource;
        private long _lastIncomingBidirectionalStreamId = -1;
        private long _lastIncomingUnidirectionalStreamId = -1;
        private Endpoint? _localEndpoint;
        private readonly object _mutex = new();
        private int _outgoingStreamCount;
        private TaskCompletionSource? _outgoingStreamsEmptySource;
        private Endpoint? _remoteEndpoint;
        private readonly ConcurrentDictionary<long, RpcStream> _streams = new();
        private bool _shutdown;

        /// <summary>Accept a new server connection. This is called after the listener accepted a new connection
        /// to perform blocking socket level initialization (TLS handshake, etc).</summary>
        /// <param name="authenticationOptions">The SSL authentication options for secure connections.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract ValueTask AcceptAsync(
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel);

        /// <summary>Accepts an incoming stream.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The accepted stream.</return>
        public abstract ValueTask<RpcStream> AcceptStreamAsync(CancellationToken cancel);

        /// <summary>Connects a new client connection. This is called after the endpoint created a new connection
        /// to establish the connection and perform blocking socket level initialization (TLS handshake, etc).
        /// </summary>
        /// <param name="authenticationOptions">The SSL authentication options for secure connections.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract ValueTask ConnectAsync(
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel);

        /// <summary>Closes the connection.</summary>
        /// <param name="errorCode">The error code indicating the reason of the connection closure.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract ValueTask CloseAsync(ConnectionErrorCode errorCode, CancellationToken cancel);

        /// <summary>Creates an outgoing stream. Depending on the transport implementation, the stream ID might not
        /// be immediately available after the stream creation. It will be available after the first successful send
        /// call on the stream.</summary>
        /// <param name="bidirectional"><c>True</c> to create a bidirectional stream, <c>false</c> otherwise.</param>
        /// <return>The outgoing stream.</return>
        public abstract RpcStream CreateStream(bool bidirectional);

        /// <summary>Releases the resources used by the connection.</summary>
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

        /// <summary>The MultiStreamConnection constructor.</summary>
        /// <param name="endpoint">The endpoint that created the connection.</param>
        /// <param name="options">The connection options.</param>
        /// <param name="logger">The logger.</param>
        protected MultiStreamConnection(
            Endpoint endpoint,
            ConnectionOptions options,
            ILogger logger)
        {
            _endpoint = endpoint;
            IsIncoming = options is ServerConnectionOptions;
            _localEndpoint = IsIncoming ? _endpoint : null;
            _remoteEndpoint = IsIncoming ? null : _endpoint;
            IncomingFrameMaxSize = options.IncomingFrameMaxSize;
            LastActivity = Time.Elapsed;
            Logger = logger;
        }

        /// <summary>Releases the resources used by the connection.</summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only
        /// unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            // Release the remaining streams.
            foreach (RpcStream stream in _streams.Values)
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

        /// <summary>Returns <c>false</c> if an incoming stream is unknown, <c>true</c> otherwise. An incoming
        /// is known if its the ID is inferior or equal to the last allocated incoming stream ID.</summary>
        protected bool IsIncomingStreamUnknown(long streamId, bool bidirectional)
        {
            lock (_mutex)
            {
                if (bidirectional)
                {
                    return streamId > _lastIncomingBidirectionalStreamId;
                }
                else
                {
                    return streamId > _lastIncomingUnidirectionalStreamId;
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

            Logger.LogReceivedData(size);
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

            if (size > 0)
            {
                Logger.LogSentData(size);
            }
        }

        /// <summary>Try to get a stream with the given ID. Transport implementations can use this method to lookup
        /// an existing stream.</summary>
        /// <param name="streamId">The stream ID.</param>
        /// <param name="value">If found, value is assigned to the stream value, null otherwise.</param>
        /// <return>True if the stream was found and value contains a non-null value, False otherwise.</return>
        protected bool TryGetStream<T>(long streamId, [NotNullWhen(returnValue: true)] out T? value)
            where T : RpcStream
        {
            if (_streams.TryGetValue(streamId, out RpcStream? stream))
            {
                value = (T)stream;
                return true;
            }
            value = null;
            return false;
        }

        internal virtual void AbortOutgoingStreams(
            RpcStreamErrorCode errorCode,
            (long Bidirectional, long Unidirectional)? ids = null)
        {
            // Abort outgoing streams with IDs larger than the given IDs, they haven't been dispatch by the peer
            // so we mark the stream as retryable. This is used by the connection to figure out whether or not the
            // request can safely be retried.
            foreach (RpcStream stream in _streams.Values)
            {
                if (!stream.IsIncoming &&
                    !stream.IsControl &&
                    (ids == null ||
                     stream.Id > (stream.IsBidirectional ? ids.Value.Bidirectional : ids.Value.Unidirectional)))
                {
                    stream.Abort(errorCode);
                }
            }
        }

        internal virtual void AbortStreams(RpcStreamErrorCode errorCode)
        {
            foreach (RpcStream stream in _streams.Values)
            {
                // Control streams are never aborted.
                if (!stream.IsControl)
                {
                    try
                    {
                        stream.Abort(errorCode);
                        stream.CancelDispatchSource?.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore
                    }
                }
            }
        }

        internal void CancelDispatch()
        {
            foreach (RpcStream stream in _streams.Values)
            {
                try
                {
                    stream.CancelDispatchSource?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore
                }
            }
        }

        internal void AddStream(long id, RpcStream stream, bool control, ref long streamId)
        {
            lock (_mutex)
            {
                // It's important to hold the mutex here to ensure the check for _streamsAborted and the stream
                // addition to the dictionary is atomic.
                if (_shutdown)
                {
                    throw new ConnectionClosedException();
                }
                _streams[id] = stream;

                // Assign the stream ID within the mutex as well to ensure that the addition of the stream to
                // the connection and the stream ID assignment are atomic.
                streamId = id;

                if (!control)
                {
                    if (stream.IsIncoming)
                    {
                        ++_incomingStreamCount;
                    }
                    else
                    {
                        ++_outgoingStreamCount;
                    }
                }

                // Keep track of the last assigned stream ID. This is used for the shutdown logic to tell the peer
                // which streams were received last.
                if (stream.IsIncoming)
                {
                    if (stream.IsBidirectional)
                    {
                        _lastIncomingBidirectionalStreamId = id;
                    }
                    else
                    {
                        _lastIncomingUnidirectionalStreamId = id;
                    }
                }
            }
        }

        internal virtual async ValueTask<RpcStream> ReceiveInitializeFrameAsync(CancellationToken cancel = default)
        {
            RpcStream stream = await AcceptStreamAsync(cancel).ConfigureAwait(false);
            Debug.Assert(stream.IsControl); // The first stream is always the control stream
            await stream.ReceiveInitializeFrameAsync(cancel).ConfigureAwait(false);
            return stream;
        }

        internal bool RemoveStream(long id)
        {
            lock (_mutex)
            {
                if (_streams.TryRemove(id, out RpcStream? stream))
                {
                    if (!stream.IsControl)
                    {
                        if (stream.IsIncoming)
                        {
                            if (--_incomingStreamCount == 0)
                            {
                                _incomingStreamsEmptySource?.SetResult();
                            }
                        }
                        else
                        {
                            if (--_outgoingStreamCount == 0)
                            {
                                _outgoingStreamsEmptySource?.SetResult();
                            }
                        }
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        internal virtual async ValueTask<RpcStream> SendInitializeFrameAsync(CancellationToken cancel = default)
        {
            RpcStream stream = CreateStream(bidirectional: false);
            Debug.Assert(stream.IsControl); // The first stream is always the control stream
            await stream.SendInitializeFrameAsync(cancel).ConfigureAwait(false);
            return stream;
        }

        internal (long Bidirectional, long Unidirectional) Shutdown()
        {
            lock (_mutex)
            {
                // Set the _shutdown flag to prevent addition of new streams by AddStream. No more streams
                // will be added to _streams once this flag is true.
                _shutdown = true;
                return (_lastIncomingBidirectionalStreamId, _lastIncomingUnidirectionalStreamId);
            }
        }

        internal IDisposable? StartScope(Server? server = null) => Logger.StartConnectionScope(this, server);

        internal async ValueTask WaitForEmptyIncomingStreamsAsync(CancellationToken cancel)
        {
            lock (_mutex)
            {
                if (_incomingStreamCount == 0)
                {
                    return;
                }
                // Run the continuations asynchronously to ensure continuations are not ran from
                // the code that aborts the last stream.
                _incomingStreamsEmptySource ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            await _incomingStreamsEmptySource.Task.IceWaitAsync(cancel).ConfigureAwait(false);
        }

        internal async Task WaitForEmptyStreamsAsync(CancellationToken cancel)
        {
            await WaitForEmptyIncomingStreamsAsync(cancel).ConfigureAwait(false);

            lock (_mutex)
            {
                if (_outgoingStreamCount == 0)
                {
                    return;
                }
                // Run the continuations asynchronously to ensure continuations are not ran from
                // the code that aborts the last stream.
                _outgoingStreamsEmptySource ??=
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            await _outgoingStreamsEmptySource.Task.IceWaitAsync(cancel).ConfigureAwait(false);
        }
    }
}
