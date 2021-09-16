// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace IceRpc.Transports
{
    /// <summary>A multi-stream connection represents a network connection that provides multiple independent
    /// streams of binary data.</summary>
    public abstract class MultiStreamConnection : IMultiStreamConnection, IDisposable
    {
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

        internal TimeSpan IdleTimeout { get; set; }
        internal bool IsServer { get; }
        internal TimeSpan LastActivity { get; private set; }

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

        internal Action? PingReceived;

        // The endpoint which created the connection. If it's a server connection, it's the local endpoint or
        // the remote endpoint otherwise.
        private int _incomingStreamCount;
        private long _lastIncomingBidirectionalStreamId = -1;
        private long _lastIncomingUnidirectionalStreamId = -1;
        private readonly object _mutex = new();
        private int _outgoingStreamCount;
        private readonly ConcurrentDictionary<long, NetworkStream> _streams = new();
        private bool _shutdown;
        private Action? _streamRemoved;

        /// <inheritdoc/>
        public abstract ValueTask<INetworkStream> AcceptStreamAsync(CancellationToken cancel);

        /// <inheritdoc/>
        public abstract INetworkStream CreateStream(bool bidirectional);

        /// <inheritdoc/>
        public abstract ValueTask InitializeAsync(CancellationToken cancel);

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>The MultiStreamConnection constructor.</summary>
        /// <param name="isServer">The connection is a server connection.</param>
        protected MultiStreamConnection(bool isServer)
        {
            IsServer = isServer;
            LastActivity = Time.Elapsed;
        }

        /// <summary>Releases the resources used by the connection.</summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only
        /// unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            // Shutdown the connection to ensure stream creation fails after the connection is disposed.
            _ = Shutdown();

            foreach (NetworkStream stream in _streams.Values)
            {
                try
                {
                    stream.Abort(StreamError.ConnectionAborted);
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

        /// <summary>Traces the given received amount of data. Transport implementations should call this method
        /// to trace the received data.</summary>
        /// <param name="buffer">The received data.</param>
        internal void Received(ReadOnlyMemory<byte> buffer)
        {
            lock (_mutex)
            {
                LastActivity = Time.Elapsed;
            }
        }

        /// <summary>Traces the given sent amount of data. Transport implementations should call this method to
        /// trace the data sent.</summary>
        /// <param name="buffers">The buffers sent.</param>
        internal void Sent(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
        {
            lock (_mutex)
            {
                LastActivity = Time.Elapsed;
            }
        }

        /// <summary>Try to get a stream with the given ID. Transport implementations can use this method to lookup
        /// an existing stream.</summary>
        /// <param name="streamId">The stream ID.</param>
        /// <param name="value">If found, value is assigned to the stream value, null otherwise.</param>
        /// <return>True if the stream was found and value contains a non-null value, False otherwise.</return>
        protected bool TryGetStream<T>(long streamId, [NotNullWhen(returnValue: true)] out T? value)
            where T : NetworkStream
        {
            if (_streams.TryGetValue(streamId, out NetworkStream? stream))
            {
                value = (T)stream;
                return true;
            }
            value = null;
            return false;
        }

        /// <summary>Ping</summary>
        public abstract Task PingAsync(CancellationToken cancel);

        internal void AbortOutgoingStreams(
            StreamError errorCode,
            (long Bidirectional, long Unidirectional) ids)
        {
            // Abort outgoing streams with IDs larger than the given IDs, they haven't been dispatch by the peer.
            foreach (NetworkStream stream in _streams.Values)
            {
                if (!stream.IsIncoming && stream.Id > (stream.IsBidirectional ? ids.Bidirectional : ids.Unidirectional))
                {
                    stream.Abort(errorCode);
                }
            }
        }

        internal void CancelDispatch()
        {
            foreach (NetworkStream stream in _streams.Values)
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

        internal void AddStream(long id, NetworkStream stream, ref long streamId)
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

                if (stream.IsIncoming)
                {
                    ++_incomingStreamCount;
                }
                else
                {
                    ++_outgoingStreamCount;
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

        internal void RemoveStream(long id)
        {
            lock (_mutex)
            {
                if (_streams.TryRemove(id, out NetworkStream? stream))
                {
                    if (stream.IsIncoming)
                    {
                        --_incomingStreamCount;
                    }
                    else
                    {
                        --_outgoingStreamCount;
                    }
                    _streamRemoved?.Invoke();
                }
                else
                {
                    Debug.Assert(false);
                }
            }
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

        internal async ValueTask WaitForStreamCountAsync(
            int outgoingStreamCount,
            int incomingStreamCount,
            CancellationToken cancel)
        {
            TaskCompletionSource? taskSource = null;
            lock (_mutex)
            {
                if (_outgoingStreamCount > outgoingStreamCount || _incomingStreamCount > incomingStreamCount)
                {
                    taskSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _streamRemoved = () =>
                        {
                            if (_outgoingStreamCount <= outgoingStreamCount &&
                                _incomingStreamCount <= incomingStreamCount)
                            {
                                taskSource.SetResult();
                                _streamRemoved = null;
                            }
                        };
                }
            }

            if (taskSource?.Task is Task task)
            {
                await task.WaitAsync(cancel).ConfigureAwait(false);
            }
        }
    }
}
