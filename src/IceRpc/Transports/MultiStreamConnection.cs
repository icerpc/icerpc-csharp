// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Text;

namespace IceRpc.Transports
{
    /// <summary>A multi-stream connection represents a network connection that provides multiple independent streams of
    /// binary data.</summary>
    /// <seealso cref="RpcStream"/>
    public abstract class MultiStreamConnection : IDisposable
    {
        /// <summary>Gets or set the idle timeout.</summary>
        public TimeSpan IdleTimeout { get; set; }

        /// <summary><c>true</c> for datagram connection; <c>false</c> otherwise.</summary>
        public abstract bool IsDatagram { get; }

        /// <summary>Indicates whether or not this connection's transport is secure.</summary>
        /// <value><c>true</c> means the connection's transport is secure. <c>false</c> means the connection's
        /// transport is not secure. If the connection is not established, secure is always <c>false</c>.</value>
        public abstract bool IsSecure { get; }

        /// <summary><c>true</c> for server connections; otherwise, <c>false</c>. A server connection is created
        /// by a server-side listener while a client connection is created from the endpoint by the client-side.
        /// </summary>
        public bool IsServer { get; }

        /// <summary>The local endpoint. The endpoint may not be available until the connection is connected.
        /// </summary>
        public Endpoint? LocalEndpoint { get; protected set; }

        /// <summary>The Ice protocol used by this connection.</summary>
        public Protocol Protocol => _endpoint.Protocol;

        /// <summary>The remote endpoint. This endpoint may not be available until the connection is accepted.
        /// </summary>
        public Endpoint? RemoteEndpoint { get; protected set; }

        /// <summary>The name of the transport.</summary>
        public string Transport => _endpoint.Transport;

        // TODO: refactor once we add a protocol abstraction. This setting has nothing to do here. It's there
        // for now because the RpcStream class implements protocol framing.
        internal int IncomingFrameMaxSize { get; set; } = 1024 * 1024;

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

        // The endpoint which created the connection. If it's a server connection, it's the local endpoint or
        // the remote endpoint otherwise.
        private readonly Endpoint _endpoint;
        private int _incomingStreamCount;
        private TaskCompletionSource? _incomingStreamsEmptySource;
        private long _lastIncomingBidirectionalStreamId = -1;
        private long _lastIncomingUnidirectionalStreamId = -1;
        private readonly object _mutex = new();
        private int _outgoingStreamCount;
        private TaskCompletionSource? _outgoingStreamsEmptySource;
        private readonly ConcurrentDictionary<long, RpcStream> _streams = new();
        private bool _shutdown;

        /// <summary>Accepts an incoming stream.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The accepted stream.</return>
        public abstract ValueTask<RpcStream> AcceptStreamAsync(CancellationToken cancel);

        /// <summary>Connects a new client connection. This is called after the endpoint created a new connection
        /// to establish the connection and perform blocking socket level initialization (TLS handshake, etc).
        /// </summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract ValueTask ConnectAsync(CancellationToken cancel);

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

        /// <summary>Checks if the parameters of the provided endpoint are compatible with this connection. Compatible
        /// means a client could reuse this connection instead of establishing a new connection.</summary>
        /// <param name="remoteEndpoint">The endpoint to check.</param>
        /// <returns><c>true</c> when this connection is a client connection whose parameters are compatible with the
        /// parameters of the provided endpoint; otherwise, <c>false</c>.</returns>
        public abstract bool HasCompatibleParams(Endpoint remoteEndpoint);

        /// <summary>Initializes the transport.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract ValueTask InitializeAsync(CancellationToken cancel);

        /// <summary>Sends a ping frame to defer the idle timeout.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract Task PingAsync(CancellationToken cancel);

        /// <inheritdoc/>
        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append(GetType().Name);
            builder.Append(" { ");
            if (PrintMembers(builder))
            {
                builder.Append(' ');
            }
            builder.Append('}');
            return builder.ToString();
        }

        /// <summary>The MultiStreamConnection constructor.</summary>
        /// <param name="endpoint">The endpoint that created the connection.</param>
        /// <param name="isServer">The connection is a server connection.</param>
        /// <param name="logger">The logger.</param>
        protected MultiStreamConnection(
            Endpoint endpoint,
            bool isServer,
            ILogger logger)
        {
            _endpoint = endpoint;
            IsServer = isServer;
            LocalEndpoint = IsServer ? _endpoint : null;
            RemoteEndpoint = IsServer ? null : _endpoint;
            LastActivity = Time.Elapsed;
            Logger = logger;
        }

        /// <summary>Releases the resources used by the connection.</summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only
        /// unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            // Shutdown the connection to ensure stream creation fails after the connection is disposed.
            _ = Shutdown();

            foreach (RpcStream stream in _streams.Values)
            {
                try
                {
                    stream.Abort(RpcStreamError.ConnectionAborted);
                }
                catch (Exception ex)
                {
                    Debug.Assert(false, $"unexpected exception on Stream.Abort: {ex}");
                }
            }
        }

        /// <summary>Returns <c>true</c> if an incoming stream is unknown, <c>false</c> otherwise. An incoming
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

        /// <summary>Prints the fields/properties of this class using the Records format.</summary>
        /// <param name="builder">The string builder.</param>
        /// <returns><c>true</c>when members are appended to the builder; otherwise, <c>false</c>.</returns>
        protected virtual bool PrintMembers(StringBuilder builder)
        {
            builder.Append("IsSecure = ").Append(IsSecure).Append(", ");
            builder.Append("IsServer = ").Append(IsServer);
            if (LocalEndpoint != null)
            {
                builder.Append(", LocalEndpoint = ").Append(LocalEndpoint);
            }
            if (RemoteEndpoint != null)
            {
                builder.Append(", RemoteEndpoint = ").Append(RemoteEndpoint);
            }
            return true;
        }

        /// <summary>Traces the given received amount of data. Transport implementations should call this method
        /// to trace the received data.</summary>
        /// <param name="buffer">The received data.</param>
        protected void Received(ReadOnlyMemory<byte> buffer)
        {
            lock (_mutex)
            {
                LastActivity = Time.Elapsed;
            }

            if (Logger.IsEnabled(LogLevel.Trace))
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Math.Min(buffer.Length, 32); ++i)
                {
                    sb.Append($"0x{buffer.Span[i]:X2} ");
                }
                if (buffer.Length > 32)
                {
                    sb.Append("...");
                }
                Logger.LogReceivedData(buffer.Length, sb.ToString().Trim());
            }
        }

        /// <summary>Traces the given sent amount of data. Transport implementations should call this method to
        /// trace the data sent.</summary>
        /// <param name="buffers">The buffers sent.</param>
        protected void Sent(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
        {
            lock (_mutex)
            {
                LastActivity = Time.Elapsed;
            }

            if (Logger.IsEnabled(LogLevel.Trace))
            {
                var sb = new StringBuilder();
                int size = 0;
                for (int i = 0; i < buffers.Length; ++i)
                {
                    ReadOnlyMemory<byte> buffer = buffers.Span[i];
                    if (size < 32)
                    {
                        for (int j = 0; j < Math.Min(buffer.Length, 32 - size); ++j)
                        {
                            sb.Append($"0x{buffer.Span[j]:X2} ");
                        }
                    }
                    size += buffer.Length;
                    if (size == 32 && i != buffers.Length)
                    {
                        sb.Append("...");
                    }
                }
                Logger.LogSentData(size, sb.ToString().Trim());
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
            RpcStreamError errorCode,
            (long Bidirectional, long Unidirectional)? ids = null)
        {
            // Abort outgoing streams with IDs larger than the given IDs, they haven't been dispatch by the peer.
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

        internal void RemoveStream(long id)
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
                }
                else
                {
                    Debug.Assert(false);
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
            await _incomingStreamsEmptySource.Task.WaitAsync(cancel).ConfigureAwait(false);
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
                _outgoingStreamsEmptySource ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            await _outgoingStreamsEmptySource.Task.WaitAsync(cancel).ConfigureAwait(false);
        }
    }
}
