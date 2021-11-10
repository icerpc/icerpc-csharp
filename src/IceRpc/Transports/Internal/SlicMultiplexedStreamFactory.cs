// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Slic stream factory implements an <see cref="IMultiplexedStreamFactory"/> on top of an <see
    /// cref="ISimpleStream"/>.</summary>
    internal class SlicMultiplexedStreamFactory : IMultiplexedStreamFactory, IDisposable
    {
        internal TimeSpan IdleTimeout { get; set; }
        internal bool IsServer { get; }
        internal int PeerPacketMaxSize { get; private set; }
        internal int PeerStreamBufferMaxSize { get; private set; }
        internal int StreamBufferMaxSize { get; }

        private int _bidirectionalStreamCount;
        private AsyncSemaphore? _bidirectionalStreamSemaphore;
        private readonly int _bidirectionalMaxStreams;

        private readonly IDisposable _disposableReader;
        private readonly IDisposable _disposableWriter;

        private long _lastRemoteBidirectionalStreamId = -1;
        private long _lastRemoteUnidirectionalStreamId = -1;
        // _mutex ensure the assignment of _lastRemoteXxx members and the addition of the stream to _streams is
        // an atomic operation.
        private readonly object _mutex = new();
        private readonly int _packetMaxSize;
        private readonly ISlicFrameReader _reader;
        private readonly ConcurrentDictionary<long, SlicMultiplexedStream> _streams = new();
        private readonly int _unidirectionalMaxStreams;
        private int _unidirectionalStreamCount;
        private AsyncSemaphore? _unidirectionalStreamSemaphore;
        private readonly ISlicFrameWriter _writer;

        public async ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel)
        {
            while (true)
            {
                (FrameType type, int dataSize, long? streamId) =
                    await _reader.ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

                switch (type)
                {
                    case FrameType.Close:
                    {
                        await _reader.ReadCloseAsync(dataSize, cancel).ConfigureAwait(false);
                        throw new ConnectionClosedException("connection closed by peer");
                    }
                    case FrameType.Stream:
                    case FrameType.StreamLast:
                    {
                        Debug.Assert(streamId != null); // The frame reader ensures that ID is set for stream frames.

                        bool endStream = type == FrameType.StreamLast;
                        if (dataSize == 0 && type == FrameType.Stream)
                        {
                            throw new InvalidDataException("received empty stream frame");
                        }

                        bool isRemote = streamId % 2 == (IsServer ? 0 : 1);
                        bool isBidirectional = streamId % 4 < 2;
                        if (TryGetStream(streamId.Value, out SlicMultiplexedStream? stream))
                        {
                            // Let the stream receive the data.
                            await stream.ReceivedFrameAsync(dataSize, endStream).ConfigureAwait(false);
                        }
                        else if (isRemote && !IsKnownRemoteStream(streamId.Value, isBidirectional))
                        {
                            // Create a new stream if the incoming stream is unknown (the client could be
                            // sending frames for old canceled incoming streams, these are ignored).

                            if (dataSize == 0)
                            {
                                throw new InvalidDataException("received empty stream frame on new stream");
                            }

                            // Accept the new incoming stream and notify the stream that data is available.
                            try
                            {
                                stream = new SlicMultiplexedStream(
                                    this,
                                    isBidirectional,
                                    remote: true,
                                    _reader,
                                    _writer);
                                AddStream(streamId.Value, stream);
                            }
                            catch
                            {
                                // The stream factory is being closed, we make sure to receive the frame data. When the
                                // factory is being closed gracefully, the factory waits from the single stream to be
                                // closed by the peer so it's important to receive and skip all the data until the
                                // single stream is closed.
                                await _reader.SkipStreamDataAsync(dataSize, cancel).ConfigureAwait(false);
                                continue;
                            }

                            if (isBidirectional)
                            {
                                if (_bidirectionalStreamCount == _bidirectionalMaxStreams)
                                {
                                    throw new InvalidDataException(
                                        $"maximum bidirectional stream count {_bidirectionalMaxStreams} reached");
                                }
                                Interlocked.Increment(ref _bidirectionalStreamCount);
                            }
                            else
                            {
                                if (_unidirectionalStreamCount == _unidirectionalMaxStreams)
                                {
                                    throw new InvalidDataException(
                                        $"maximum unidirectional stream count {_unidirectionalMaxStreams} reached");
                                }
                                Interlocked.Increment(ref _unidirectionalStreamCount);
                            }

                            // Let the stream receive the data.
                            await stream.ReceivedFrameAsync(dataSize, endStream).ConfigureAwait(false);
                            return stream;
                        }
                        else if (!isBidirectional && endStream)
                        {
                            // Release the stream count for the unidirectional stream.
                            _unidirectionalStreamSemaphore!.Release();
                        }
                        else
                        {
                            throw new InvalidDataException($"received stream frame for unknown stream ID={streamId}");
                        }
                        break;
                    }
                    case FrameType.StreamConsumed:
                    {
                        Debug.Assert(streamId != null); // The frame reader ensures that ID is set for stream frames.

                        if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream consumed frame too large");
                        }

                        StreamConsumedBody streamConsumed =
                            await _reader.ReadStreamConsumedAsync(dataSize, cancel).ConfigureAwait(false);
                        if (TryGetStream(streamId.Value, out SlicMultiplexedStream? stream))
                        {
                            stream.ReceivedConsumed((int)streamConsumed.Size);
                        }
                        break;
                    }
                    case FrameType.StreamReset:
                    {
                        Debug.Assert(streamId != null); // The frame reader ensures that ID is set for stream frames.

                        if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream reset frame too large");
                        }

                        StreamResetBody streamReset =
                            await _reader.ReadStreamResetAsync(dataSize, cancel).ConfigureAwait(false);
                        if (TryGetStream(streamId.Value, out SlicMultiplexedStream? stream))
                        {
                            stream.ReceivedReset((StreamError)streamReset.ApplicationProtocolErrorCode);
                        }
                        break;
                    }
                    case FrameType.StreamStopSending:
                    {
                        Debug.Assert(streamId != null); // The frame reader ensures that ID is set for stream frames.

                        if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream stop sending frame too large");
                        }

                        StreamStopSendingBody _ =
                            await _reader.ReadStreamStopSendingAsync(dataSize, cancel).ConfigureAwait(false);
                        if (TryGetStream(streamId.Value, out SlicMultiplexedStream? stream))
                        {
                            stream.TrySetWriteCompleted();
                        }
                        break;
                    }
                    default:
                    {
                        throw new InvalidDataException($"unexpected Slic frame with frame type '{type}'");
                    }
                }
            }

            bool IsKnownRemoteStream(long streamId, bool bidirectional)
            {
                lock (_mutex)
                {
                    if (bidirectional)
                    {
                        return streamId <= _lastRemoteBidirectionalStreamId;
                    }
                    else
                    {
                        return streamId <= _lastRemoteUnidirectionalStreamId;
                    }
                }
            }
        }

        public ValueTask CloseAsync(CancellationToken cancel) => _writer.WriteCloseAsync(cancel);

        public IMultiplexedStream CreateStream(bool bidirectional) =>
            new SlicMultiplexedStream(this, bidirectional, remote: false, _reader, _writer);

        public void Dispose()
        {
            // Unblock requests waiting on the semaphores.
            var exception = new ConnectionClosedException();
            _bidirectionalStreamSemaphore?.Complete(exception);
            _unidirectionalStreamSemaphore?.Complete(exception);

            // Abort streams.
            foreach (SlicMultiplexedStream stream in _streams.Values)
            {
                try
                {
                    ((IMultiplexedStream)stream).Abort(StreamError.ConnectionAborted);
                }
                catch (Exception ex)
                {
                    Debug.Assert(false, $"unexpected exception on Stream.Abort: {ex}");
                }
            }

            _disposableReader.Dispose();
            _disposableWriter.Dispose();
        }

        internal SlicMultiplexedStreamFactory(
            ISimpleStream simpleStream,
            Func<ISlicFrameReader, ISlicFrameReader> slicFrameReaderDecorator,
            Func<ISlicFrameWriter, ISlicFrameWriter> slicFrameWriterDecorator,
            bool isServer,
            TimeSpan idleTimeout,
            SlicOptions options)
        {
            IdleTimeout = idleTimeout;
            IsServer = isServer;

            var reader = new StreamSlicFrameReader(simpleStream);
            _disposableReader = reader;
            _reader = slicFrameReaderDecorator(reader);

            var writer = new SynchronizedSlicFrameWriterDecorator(
                slicFrameWriterDecorator(new StreamSlicFrameWriter(simpleStream)),
                this);

            _disposableWriter = writer;
            _writer = writer;

            _packetMaxSize = options.PacketMaxSize;
            StreamBufferMaxSize = options.StreamBufferMaxSize;

            // Initially set the peer packet max size to the local max size to ensure we can receive the first
            // initialize frame.
            PeerPacketMaxSize = _packetMaxSize;
            PeerStreamBufferMaxSize = StreamBufferMaxSize;

            // Configure the maximum stream counts to ensure the peer won't open more than one stream.
            _bidirectionalMaxStreams = options.BidirectionalStreamMaxCount;
            _unidirectionalMaxStreams = options.UnidirectionalStreamMaxCount;
        }

        internal void AddStream(long id, SlicMultiplexedStream stream)
        {
            lock (_mutex)
            {
                _streams[id] = stream;

                // Assign the stream ID within the mutex to ensure that the addition of the stream to the
                // connection and the stream ID assignment are atomic.
                stream.Id = id;

                // Keep track of the last assigned stream ID. This is used to figure out if the stream is
                // known or unknown.
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

        internal async ValueTask InitializeAsync(CancellationToken cancel)
        {
            FrameType type;
            int dataSize;

            if (IsServer)
            {
                // Read the Initialize frame sent by the client.
                (type, dataSize, _) = await _reader.ReadFrameHeaderAsync(cancel).ConfigureAwait(false);
                (uint version, InitializeBody? initializeBody) = await _reader.ReadInitializeAsync(
                    type,
                    dataSize,
                    cancel).ConfigureAwait(false);

                if (version != 1)
                {
                    // Unsupported version, try to negotiate another version by sending a Version frame with
                    // the Slic versions supported by this server.
                    var versionBody = new VersionBody(new uint[] { SlicDefinitions.V1 });
                    await _writer.WriteVersionAsync(versionBody, cancel).ConfigureAwait(false);

                    // Read again the Initialize frame sent by the client.
                    (type, dataSize, _) = await _reader.ReadFrameHeaderAsync(cancel).ConfigureAwait(false);
                    (version, initializeBody) = await _reader.ReadInitializeAsync(
                        type,
                        dataSize,
                        cancel).ConfigureAwait(false);
                }

                if (initializeBody == null)
                {
                    throw new InvalidDataException($"unsupported Slic version '{version}'");
                }

                // Check the application protocol and set the parameters.
                try
                {
                    if (Protocol.Parse(initializeBody.Value.ApplicationProtocolName) != Protocol.Ice2)
                    {
                        throw new NotSupportedException(
                            $"application protocol '{initializeBody.Value.ApplicationProtocolName}' is not supported");
                    }
                }
                catch (FormatException ex)
                {
                    throw new NotSupportedException(
                        $"unknown application protocol '{initializeBody.Value.ApplicationProtocolName}'", ex);
                }
                SetParameters(initializeBody.Value.Parameters);

                // Write back an InitializeAck frame.
                var initializeAck = new InitializeAckBody(GetParameters());
                await _writer.WriteInitializeAckAsync(initializeAck, cancel).ConfigureAwait(false);
            }
            else
            {
                // Write the Initialize frame.
                var initializeBody = new InitializeBody(Protocol.Ice2.Name, GetParameters());
                await _writer.WriteInitializeAsync(SlicDefinitions.V1, initializeBody, cancel).ConfigureAwait(false);

                // Read back either the InitializeAck or Version frame.
                (type, dataSize, _) = await _reader.ReadFrameHeaderAsync(cancel).ConfigureAwait(false);
                (InitializeAckBody? initializeAckBody, VersionBody? versionBody) =
                    await _reader.ReadInitializeAckOrVersionAsync(type, dataSize, cancel).ConfigureAwait(false);

                if (initializeAckBody != null)
                {
                    SetParameters(initializeAckBody.Value.Parameters);
                }
                else if (versionBody != null)
                {
                    // We currently only support V1
                    throw new InvalidDataException(
                        $"unsupported Slic versions '{string.Join(", ", versionBody.Value.Versions)}'");
                }
            }
        }

        internal void ReleaseStream(SlicMultiplexedStream stream)
        {
            Debug.Assert(stream.IsStarted);

            _streams.TryRemove(stream.Id, out SlicMultiplexedStream? _);

            if (stream.IsRemote)
            {
                if (stream.IsBidirectional)
                {
                    Interlocked.Decrement(ref _bidirectionalStreamCount);
                }
                else
                {
                    Interlocked.Decrement(ref _unidirectionalStreamCount);
                }
            }
            else if (stream.IsBidirectional)
            {
                _bidirectionalStreamSemaphore!.Release();
            }
            else
            {
                // Don't release the semaphore for unidirectional streams. The semaphore will be released
                // by AcceptStreamAsync when the peer sends a StreamLast frame.
            }
        }

        internal async ValueTask SendStreamFrameAsync(
            SlicMultiplexedStream stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            AsyncSemaphore streamSemaphore = stream.IsBidirectional ?
                _bidirectionalStreamSemaphore! :
                _unidirectionalStreamSemaphore!;

            if (!stream.IsStarted)
            {
                // If the outgoing stream isn't started, we need to acquire the stream semaphore to ensure we
                // don't open more streams than the peer allows.
                await streamSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            }

            try
            {
                await _writer.WriteStreamFrameAsync(stream, buffers, endStream, cancel).ConfigureAwait(false);
            }
            catch
            {
                if (!stream.IsStarted)
                {
                    // If the stream is still not started, release the semaphore.
                    streamSemaphore.Release();
                }
            }
        }

        private Dictionary<int, IList<byte>> GetParameters()
        {
            var parameters = new List<KeyValuePair<int, IList<byte>>>
                {
                    EncodeParameter(ParameterKey.MaxBidirectionalStreams, (ulong)_bidirectionalMaxStreams),
                    EncodeParameter(ParameterKey.MaxUnidirectionalStreams, (ulong)_unidirectionalMaxStreams),
                    EncodeParameter(ParameterKey.PacketMaxSize, (ulong)_packetMaxSize),
                    EncodeParameter(ParameterKey.StreamBufferMaxSize, (ulong)StreamBufferMaxSize)
                };
            if (IdleTimeout != TimeSpan.MaxValue && IdleTimeout != Timeout.InfiniteTimeSpan)
            {
                parameters.Add(EncodeParameter(ParameterKey.IdleTimeout, (ulong)IdleTimeout.TotalMilliseconds));
            }
            return new Dictionary<int, IList<byte>>(parameters);

            static KeyValuePair<int, IList<byte>> EncodeParameter(ParameterKey key, ulong value)
            {
                var bufferWriter = new BufferWriter();
                var encoder = new Ice20Encoder(bufferWriter);
                encoder.EncodeVarULong(value);
                return new((int)key, bufferWriter.Finish().ToSingleBuffer().ToArray());
            }
        }

        private void SetParameters(IDictionary<int, IList<byte>> parameters)
        {
            TimeSpan? peerIdleTimeout = null;

            foreach ((ParameterKey key, ulong value) in parameters.DecodedParameters())
            {
                if (key == ParameterKey.MaxBidirectionalStreams)
                {
                    _bidirectionalStreamSemaphore = new AsyncSemaphore((int)value);
                }
                else if (key == ParameterKey.MaxUnidirectionalStreams)
                {
                    _unidirectionalStreamSemaphore = new AsyncSemaphore((int)value);
                }
                else if (key == ParameterKey.IdleTimeout)
                {
                    // Use the smallest idle timeout.
                    peerIdleTimeout = TimeSpan.FromMilliseconds(value);
                }
                else if (key == ParameterKey.PacketMaxSize)
                {
                    PeerPacketMaxSize = (int)value;
                }
                else if (key == ParameterKey.StreamBufferMaxSize)
                {
                    PeerStreamBufferMaxSize = (int)value;
                }
                else
                {
                    // Ignore unsupported parameters
                }
            }

            // Now, ensure required parameters are set.

            if (_bidirectionalStreamSemaphore == null)
            {
                throw new InvalidDataException("missing MaxBidirectionalStreams Slic connection parameter");
            }

            if (_unidirectionalStreamSemaphore == null)
            {
                throw new InvalidDataException("missing MaxUnidirectionalStreams Slic connection parameter");
            }

            peerIdleTimeout ??= TimeSpan.MaxValue;
            if (IdleTimeout == Timeout.InfiniteTimeSpan || peerIdleTimeout < IdleTimeout)
            {
                IdleTimeout = peerIdleTimeout.Value;
            }

            if (PeerPacketMaxSize < 1024)
            {
                throw new InvalidDataException($"invalid PacketMaxSize={PeerPacketMaxSize} Slic connection parameter");
            }
        }

        private bool TryGetStream(long streamId, [NotNullWhen(returnValue: true)] out SlicMultiplexedStream? value)
        {
            if (_streams.TryGetValue(streamId, out SlicMultiplexedStream? stream))
            {
                value = stream;
                return true;
            }
            value = null;
            return false;
        }
    }
}
