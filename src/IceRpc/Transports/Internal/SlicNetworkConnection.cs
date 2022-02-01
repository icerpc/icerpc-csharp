// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Slice;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Slic multiplexed network connection implements an <see cref="IMultiplexedNetworkConnection"/> on
    /// top of a <see cref="ISimpleNetworkConnection"/>.</summary>
    internal class SlicNetworkConnection : IMultiplexedNetworkConnection
    {
        public bool IsSecure => _simpleNetworkConnection.IsSecure;

        public TimeSpan LastActivity => _simpleNetworkConnection.LastActivity;

        internal TimeSpan IdleTimeout { get; set; }
        internal bool IsServer { get; }
        internal int PeerPacketMaxSize { get; private set; }
        internal int PeerPauseWriterThreshold { get; private set; }
        internal int PauseWriterThreshold { get; }
        internal int ResumeWriterThreshold { get; }
        internal MemoryPool<byte> Pool { get; }
        internal int MinimumSegmentSize { get; }

        private readonly AsyncQueue<IMultiplexedStream> _acceptedStreamQueue = new();
        private int _bidirectionalStreamCount;
        private AsyncSemaphore? _bidirectionalStreamSemaphore;
        private readonly int _bidirectionalMaxStreams;
        private bool _isDisposed;
        private readonly IDisposable _disposableReader;
        private readonly IDisposable _disposableWriter;

        private long _lastRemoteBidirectionalStreamId = -1;
        private long _lastRemoteUnidirectionalStreamId = -1;
        // _mutex ensure the assignment of _lastRemoteXxx members and the addition of the stream to _streams is
        // an atomic operation.
        private readonly object _mutex = new();
        private readonly int _packetMaxSize;
        private readonly ISlicFrameReader _reader;
        private readonly ReadOnlyMemory<byte> _sendHeader;
        private readonly List<ReadOnlyMemory<byte>> _sendBuffers = new(16);
        private readonly ISimpleNetworkConnection _simpleNetworkConnection;
        private readonly ConcurrentDictionary<long, SlicMultiplexedStream> _streams = new();
        private readonly int _unidirectionalMaxStreams;
        private int _unidirectionalStreamCount;
        private AsyncSemaphore? _unidirectionalStreamSemaphore;
        private readonly ISlicFrameWriter _writer;

        public ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel) =>
            _acceptedStreamQueue.DequeueAsync(cancel);

        public async Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel)
        {
            // Connect the simple network connection.
            NetworkConnectionInformation information = await _simpleNetworkConnection.ConnectAsync(
                cancel).ConfigureAwait(false);

            // The initial Slic idle timeout is the simple connection idle timeout.
            IdleTimeout = information.IdleTimeout;

            // Initialize the Slic connection.
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
                    if (Protocol.FromString(initializeBody.Value.ApplicationProtocolName) != Protocol.IceRpc)
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
                var initializeBody = new InitializeBody(Protocol.IceRpc.Name, GetParameters());
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

            // Start a task to read frames from the network connection.
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await ReadFramesAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        _acceptedStreamQueue.TryComplete(exception);
                    }
                },
                CancellationToken.None);

            return information with { IdleTimeout = IdleTimeout };
        }

        public IMultiplexedStream CreateStream(bool bidirectional) =>
            // TODO: Cache SliceMultiplexedStream
            new SlicMultiplexedStream(this, bidirectional, remote: false, _reader, _writer);

        public async ValueTask DisposeAsync()
        {
            lock (_mutex)
            {
                _isDisposed = true;
            }

            await _simpleNetworkConnection.DisposeAsync().ConfigureAwait(false);

            // Unblock requests waiting on the semaphores.
            var exception = new ObjectDisposedException($"{typeof(IMultiplexedNetworkConnection)}:{this}");
            _bidirectionalStreamSemaphore?.Complete(exception);
            _unidirectionalStreamSemaphore?.Complete(exception);

            foreach (SlicMultiplexedStream stream in _streams.Values)
            {
                stream.Abort();
            }

            // Unblock task blocked on AcceptStreamAsync
            _acceptedStreamQueue.TryComplete(exception);

            _disposableReader.Dispose();
            _disposableWriter.Dispose();
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            _simpleNetworkConnection.HasCompatibleParams(remoteEndpoint);

        internal SlicNetworkConnection(
            ISimpleNetworkConnection simpleNetworkConnection,
            bool isServer,
            Func<ISlicFrameReader, ISlicFrameReader> slicFrameReaderDecorator,
            Func<ISlicFrameWriter, ISlicFrameWriter> slicFrameWriterDecorator,
            SlicOptions slicOptions)
        {
            IsServer = isServer;

            var reader = new SlicFrameReader(simpleNetworkConnection.ReadAsync);
            _disposableReader = reader;
            _reader = slicFrameReaderDecorator(reader);

            var writer = new SynchronizedSlicFrameWriterDecorator(
                slicFrameWriterDecorator(new SlicFrameWriter(simpleNetworkConnection.WriteAsync)),
                this);

            _disposableWriter = writer;
            _writer = writer;

            _simpleNetworkConnection = simpleNetworkConnection;
            _sendHeader = SlicDefinitions.FrameHeader.ToArray();
            _packetMaxSize = slicOptions.PacketMaxSize;
            PauseWriterThreshold = slicOptions.PauseWriterThreshold;
            ResumeWriterThreshold = slicOptions.ResumeWriterThreshold;

            // Initially set the peer packet max size to the local max size to ensure we can receive the first
            // initialize frame.
            PeerPacketMaxSize = _packetMaxSize;
            PeerPauseWriterThreshold = PauseWriterThreshold;

            // Configure the maximum stream counts to ensure the peer won't open more than one stream.
            _bidirectionalMaxStreams = slicOptions.BidirectionalStreamMaxCount;
            _unidirectionalMaxStreams = slicOptions.UnidirectionalStreamMaxCount;

            Pool = slicOptions.Pool;
            MinimumSegmentSize = slicOptions.MinimumSegmentSize;
        }

        internal void AddStream(long id, SlicMultiplexedStream stream)
        {
            lock (_mutex)
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException($"{typeof(IMultiplexedNetworkConnection)}:{this}");
                }

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

        // internal async ValueTask<FlushResult> SendFrameAsync<T>(
        //     SlicMultiplexedStream? stream,
        //     FrameType frameType,
        //     T? frame,
        //     CancellationToken cancel) where T : struct
        // {
        // }

        internal async ValueTask<FlushResult> SendStreamFrameAsync(
            SlicMultiplexedStream stream,
            ReadOnlySequence<byte> source1,
            ReadOnlySequence<byte> source2,
            bool endStream,
            CancellationToken cancel)
        {
            bool sendingSource1 = true;
            ReadOnlySequence<byte> sendSource = source1;
            do
            {
                // Check if writes completed, the stream might have been reset by the peer. Don't send the data and
                // return a completed flush result.
                if (stream.WritesCompleted)
                {
                    if (stream.ResetError is long error &&
                        error.ToSlicError() is SlicStreamError slicError &&
                        slicError != SlicStreamError.NoError)
                    {
                        throw new MultiplexedStreamAbortedException(error);
                    }
                    else
                    {
                        return new FlushResult(isCanceled: false, isCompleted: true);
                    }
                }

                // Acquire send credit. If no send credit is available, this will block until the receiver allows
                // sending additional data.
                int sendCredit = await stream.SendCreditAcquireAsync(cancel).ConfigureAwait(false);

                // Gather the next buffers into _sendBuffers to send the stream frame. We get up to sendCredit bytes
                // from the internal buffer or given source. If there are more bytes to send they will be sent into a
                // separate packet once the peer sends the StreamResumeWrite frame to provide additional send credit.
                int sendSize = 0;
                int sendMaxSize = Math.Min(sendCredit, PeerPacketMaxSize);
                _sendBuffers.Clear();
                _sendBuffers.Add(_sendHeader);
                while (sendSize < sendMaxSize)
                {
                    if (sendingSource1 && sendSource.IsEmpty)
                    {
                        // Switch to sending the given source since we've consumed all the data from the internal
                        // pipe.
                        sendingSource1 = false;
                        sendSource = source2;
                    }

                    if (sendSource.IsEmpty)
                    {
                        // No more data to send!
                        break;
                    }

                    // Add the send source data to the send buffers.
                    sendSize += FillSendBuffers(ref sendSource, sendMaxSize - sendSize);
                }

                try
                {
                    // Notify the stream that we're consuming sendSize credit. It's important to call this before
                    // sending the stream frame to avoid race conditions where the StreamResumeWrite frame could be
                    // received before the send credit was updated.
                    stream.SendCreditConsumed(sendSize);

                    // Send the stream frame.
                    if (sendSize > 0 || endStream)
                    {
                        AsyncSemaphore streamSemaphore = stream.IsBidirectional ?
                            _bidirectionalStreamSemaphore! :
                            _unidirectionalStreamSemaphore!;

                        if (!stream.IsStarted)
                        {
                            // If the outgoing stream isn't started, we need to acquire the stream semaphore to ensure
                            // we don't open more streams than the peer allows.
                            await streamSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                        }

                        try
                        {
                            // The writer WriteStreamFrameAsync method requires the header to always be included as the
                            // first buffers of the send buffers. This avoids allocating a new ReadOnlyMemory<byte>
                            // array to append the header.
                            Debug.Assert(_sendBuffers.Count > 0);
                            Debug.Assert(_sendBuffers[0].Length == _sendHeader.Length);
                            await _writer.WriteStreamFrameAsync(
                                stream,
                                _sendBuffers,
                                endStream && !sendingSource1 && sendSource.IsEmpty,
                                cancel).ConfigureAwait(false);
                        }
                        catch
                        {
                            if (!stream.IsStarted)
                            {
                                // If the stream is still not started, release the semaphore.
                                streamSemaphore.Release();
                            }
                            throw;
                        }
                    }
                }
                catch (MultiplexedStreamAbortedException ex)
                {
                    if (ex.ToSlicError() == SlicStreamError.NoError)
                    {
                        return new FlushResult(isCanceled: false, isCompleted: true);
                    }
                    else
                    {
                        throw;
                    }
                }
                // TODO: XXX
                // catch (Exception ex)
                // {
                //     _sendSemaphore.Complete(ex);
                //     throw;
                // }
            }
            while (sendingSource1 || !sendSource.IsEmpty);

            return new FlushResult(isCanceled: false, isCompleted: false);

            int FillSendBuffers(ref ReadOnlySequence<byte> source, int maxSize)
            {
                Debug.Assert(maxSize > 0);
                int size = 0;
                SequencePosition position = source.Start;
                while (true)
                {
                    if (!source.TryGet(ref position, out ReadOnlyMemory<byte> memory))
                    {
                        // No more data available.
                        source = ReadOnlySequence<byte>.Empty;
                        return size;
                    }

                    if (size + memory.Length < maxSize)
                    {
                        // Copy the segment to the send buffers.
                        _sendBuffers.Add(memory);
                        size += memory.Length;
                    }
                    else
                    {
                        // We've reached the maximum send size. Slice the buffer to send and slice the source buffer
                        // to the remaining data to consume.
                        _sendBuffers.Add(memory[0..(maxSize - size)]);
                        size += maxSize - size;
                        source = source.Slice(source.GetPosition(size));
                        return size;
                    }
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
                    EncodeParameter(ParameterKey.PauseWriterThreshold, (ulong)PauseWriterThreshold)
                };
            if (IdleTimeout != TimeSpan.MaxValue && IdleTimeout != Timeout.InfiniteTimeSpan)
            {
                parameters.Add(EncodeParameter(ParameterKey.IdleTimeout, (ulong)IdleTimeout.TotalMilliseconds));
            }
            return new Dictionary<int, IList<byte>>(parameters);

            static KeyValuePair<int, IList<byte>> EncodeParameter(ParameterKey key, ulong value)
            {
                int sizeLength = SliceEncoder.GetVarULongEncodedSize(value);
                byte[] buffer = new byte[sizeLength];
                SliceEncoder.EncodeVarULong(value, buffer);
                return new((int)key, buffer);
            }
        }

        private async Task ReadFramesAsync(CancellationToken cancel)
        {
            while (true)
            {
                (FrameType type, int dataSize, long? streamId) =
                    await _reader.ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

                // Only stream frames are expected at this point. Non stream frames are only exchanged at the
                // initialization step.
                if (streamId == null)
                {
                    throw new InvalidDataException($"unexpected Slic frame with frame type '{type}'");
                }

                switch (type)
                {
                    case FrameType.Stream:
                    case FrameType.StreamLast:
                    {
                        bool endStream = type == FrameType.StreamLast;
                        bool isRemote = streamId % 2 == (IsServer ? 0 : 1);
                        bool isBidirectional = streamId % 4 < 2;

                        if (!isBidirectional && !isRemote)
                        {
                            throw new InvalidDataException("received stream frame on local unidirectional stream");
                        }
                        else if (dataSize == 0 && !endStream)
                        {
                            throw new InvalidDataException(
                                "invalid stream frame, received 0 bytes without end of stream");
                        }

                        if (_streams.TryGetValue(streamId.Value, out SlicMultiplexedStream? stream))
                        {
                            // Let the stream receive the data.
                            await stream.ReceivedFrameAsync(dataSize, endStream).ConfigureAwait(false);
                        }
                        else if (isRemote && !IsKnownRemoteStream(streamId.Value, isBidirectional))
                        {
                            // Create a new stream if the remote stream is unknown.

                            if (dataSize == 0)
                            {
                                throw new InvalidDataException("received empty stream frame on new stream");
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

                            // Accept the new incoming stream.
                            // TODO: Cache SliceMultiplexedStream
                            stream = new SlicMultiplexedStream(this, isBidirectional, remote: true, _reader, _writer);
                            try
                            {
                                AddStream(streamId.Value, stream);
                            }
                            catch
                            {
                                stream.Abort();
                                throw;
                            }

                            // Let the stream receive the data.
                            await stream.ReceivedFrameAsync(dataSize, endStream).ConfigureAwait(false);

                            // Queue the new stream.
                            _acceptedStreamQueue.Enqueue(stream);
                        }
                        else
                        {
                            // The stream has been shutdown. Read and ignore the data.
                            using IMemoryOwner<byte> owner = Pool.Rent(MinimumSegmentSize);
                            int size = dataSize;
                            while (size > 0)
                            {
                                Memory<byte> chunk = owner.Memory;
                                if (chunk.Length > size)
                                {
                                    chunk = chunk[0..size];
                                }
                                await _reader.ReadFrameDataAsync(chunk, CancellationToken.None).ConfigureAwait(false);
                                size -= chunk.Length;
                            }
                        }
                        break;
                    }
                    case FrameType.StreamResumeWrite:
                    {
                        if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream resume write frame too large");
                        }

                        StreamResumeWriteBody streamConsumed =
                            await _reader.ReadStreamResumeWriteAsync(dataSize, cancel).ConfigureAwait(false);
                        if (_streams.TryGetValue(streamId.Value, out SlicMultiplexedStream? stream))
                        {
                            stream.ReceivedConsumed((int)streamConsumed.Size);
                        }
                        break;
                    }
                    case FrameType.StreamReset:
                    {
                        if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream reset frame too large");
                        }

                        StreamResetBody streamReset =
                            await _reader.ReadStreamResetAsync(dataSize, cancel).ConfigureAwait(false);
                        if (_streams.TryGetValue(streamId.Value, out SlicMultiplexedStream? stream))
                        {
                            stream.ReceivedReset(streamReset.ApplicationProtocolErrorCode);
                        }
                        break;
                    }
                    case FrameType.StreamStopSending:
                    {
                        if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream stop sending frame too large");
                        }

                        StreamStopSendingBody streamStopSending =
                            await _reader.ReadStreamStopSendingAsync(dataSize, cancel).ConfigureAwait(false);
                        if (_streams.TryGetValue(streamId.Value, out SlicMultiplexedStream? stream))
                        {
                            stream.ReceivedStopSending(streamStopSending.ApplicationProtocolErrorCode);
                        }
                        break;
                    }
                    case FrameType.UnidirectionalStreamReleased:
                    {
                        if (dataSize > 0)
                        {
                            throw new InvalidDataException("unidirectional stream released frame too large");
                        }

                        await _reader.ReadUnidirectionalStreamReleasedAsync(cancel).ConfigureAwait(false);

                        // Release the unidirectional stream semaphore for the unidirectional stream.
                        _unidirectionalStreamSemaphore!.Release();
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
                else if (key == ParameterKey.PauseWriterThreshold)
                {
                    PeerPauseWriterThreshold = (int)value;
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
    }
}
