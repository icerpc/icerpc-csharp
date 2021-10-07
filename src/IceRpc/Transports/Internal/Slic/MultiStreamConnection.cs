// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>A multi-stream connection represents a network connection that provides multiple independent
    /// streams of binary data.</summary>
    // TODO: XXX merge into SlicConnection
    public abstract class MultiStreamConnection : IMultiStreamConnection, IDisposable
    {
        internal TimeSpan IdleTimeout { get; set; }
        internal bool IsServer { get; }
        private long _lastRemoteBidirectionalStreamId = -1;
        private long _lastRemoteUnidirectionalStreamId = -1;
        // _mutex ensure the assignment of _lastRemoteXxx members and the addition of the stream to _streams is
        // an atomic operation.
        private readonly object _mutex = new();
        private readonly ConcurrentDictionary<long, NetworkStream> _streams = new();

        /// <inheritdoc/>
        public abstract ValueTask<INetworkStream> AcceptStreamAsync(CancellationToken cancel);

        /// <inheritdoc/>
        public abstract INetworkStream CreateStream(bool bidirectional);

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>The MultiStreamConnection constructor.</summary>
        /// <param name="isServer">The connection is a server connection.</param>
        protected MultiStreamConnection(bool isServer) => IsServer = isServer;

        /// <summary>Releases the resources used by the connection.</summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release
        /// only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            foreach (NetworkStream stream in _streams.Values)
            {
                try
                {
                    ((INetworkStream)stream).Abort(StreamError.ConnectionAborted);
                }
                catch (Exception ex)
                {
                    Debug.Assert(false, $"unexpected exception on Stream.Abort: {ex}");
                }
            }
        }

        /// <summary>Returns <c>true</c> if a remote stream is unknown, <c>false</c> otherwise. A remote
        /// stream is known if its ID is inferior or equal to the last allocated remote stream ID.</summary>
        protected bool IsRemoteStreamUnknown(long streamId, bool bidirectional)
        {
            lock (_mutex)
            {
                if (bidirectional)
                {
                    return streamId > _lastRemoteBidirectionalStreamId;
                }
                else
                {
                    return streamId > _lastRemoteUnidirectionalStreamId;
                }
            }
        }

        /// <summary>Try to get a stream with the given ID. Transport implementations can use this method to
        /// lookup an existing stream.</summary>
        /// <param name="streamId">The stream ID.</param>
        /// <param name="value">If found, value is assigned to the stream value, null otherwise.</param>
        /// <return>True if the stream was found and value contains a non-null value, False
        /// otherwise.</return>
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

        internal void AddStream(long id, NetworkStream stream, ref long streamId)
        {
            lock (_mutex)
            {
                _streams[id] = stream;

                // Assign the stream ID within the mutex to ensure that the addition of the stream to the
                // connection and the stream ID assignment are atomic.
                streamId = id;

                // Keep track of the last assigned stream ID. This is used for the shutdown logic to tell the peer
                // which streams were received last.
                if (stream.IsRemote)
                {
                    if (stream.IsBidirectional)
                    {
                        _lastRemoteBidirectionalStreamId = id;
                    }
                    else
                    {
                        _lastRemoteUnidirectionalStreamId = id;
                    }
                }
            }
        }

        internal void RemoveStream(long id) => _streams.TryRemove(id, out NetworkStream? _);
    }
}
