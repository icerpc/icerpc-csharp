// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
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
        public TimeSpan LastActivity => _simpleNetworkConnection.LastActivity;

        internal TimeSpan IdleTimeout { get; set; }
        internal bool IsServer { get; }
        internal int MinimumSegmentSize { get; }
        internal int PauseWriterThreshold { get; }
        internal int PeerPacketMaxSize { get; private set; }
        internal int PeerPauseWriterThreshold { get; private set; }
        internal MemoryPool<byte> Pool { get; }
        internal int ResumeWriterThreshold { get; }

        private readonly AsyncQueue<IMultiplexedStream> _acceptedStreamQueue = new();
        private int _bidirectionalStreamCount;
        private AsyncSemaphore? _bidirectionalStreamSemaphore;
        private readonly int _bidirectionalMaxStreams;
        private bool _isDisposed;
        private long _lastRemoteBidirectionalStreamId = -1;
        private long _lastRemoteUnidirectionalStreamId = -1;
        // _mutex ensure the assignment of _lastRemoteXxx members and the addition of the stream to _streams is
        // an atomic operation.
        private readonly object _mutex = new();
        private long _nextBidirectionalId;
        private long _nextUnidirectionalId;
        private readonly int _packetMaxSize;
        private readonly CancellationTokenSource _readCancellationTokenSource = new();
        private AsyncSemaphore? _readCompletedSemaphore;
        private readonly ISlicFrameReader _reader;
        private readonly AsyncSemaphore _sendSemaphore = new(1, 1);
        private readonly ISimpleNetworkConnection _simpleNetworkConnection;
        private readonly SimpleNetworkConnectionPipeReader _simpleNetworkConnectionReader;
        private readonly SimpleNetworkConnectionPipeWriter _simpleNetworkConnectionWriter;
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
                uint version;
                InitializeBody? initializeBody;
                (type, dataSize, _) = await _reader.ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

                if (dataSize == 0)
                {
                    throw new InvalidDataException("invalid empty initialize frame");
                }

                (version, initializeBody) = await ReadFrameAsync(
                    dataSize,
                    SlicMemoryExtensions.DecodeInitialize,
                    cancel).ConfigureAwait(false);

                if (version != 1)
                {
                    // Unsupported version, try to negotiate another version by sending a Version frame with
                    // the Slic versions supported by this server.
                    await SendFrameAsync(
                        stream: null,
                        FrameType.Version,
                        new VersionBody(new uint[] { SlicDefinitions.V1 }).Encode,
                        cancel).ConfigureAwait(false);

                    // Read again the Initialize frame sent by the client.
                    (type, dataSize, _) = await _reader.ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

                    if (dataSize == 0)
                    {
                        throw new InvalidDataException("invalid empty initialize frame");
                    }

                    (version, initializeBody) = await ReadFrameAsync(
                        dataSize,
                        SlicMemoryExtensions.DecodeInitialize,
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
                await SendFrameAsync(
                    stream: null,
                    FrameType.InitializeAck,
                    new InitializeAckBody(GetParameters()).Encode,
                    cancel).ConfigureAwait(false);
            }
            else
            {
                // Write the Initialize frame.
                var initializeBody = new InitializeBody(Protocol.IceRpc.Name, GetParameters());
                await SendFrameAsync(
                    stream: null,
                    FrameType.Initialize,
                    (ref SliceEncoder encoder) =>
                    {
                        encoder.EncodeVarUInt(SlicDefinitions.V1);
                        initializeBody.Encode(ref encoder);
                    },
                    cancel).ConfigureAwait(false);

                // Read back either the InitializeAck or Version frame.
                (type, dataSize, _) = await _reader.ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

                switch (type)
                {
                    case FrameType.InitializeAck:
                        InitializeAckBody initializeAckBody = await ReadFrameAsync(
                            dataSize,
                            (ref SliceDecoder decoder) => new InitializeAckBody(ref decoder),
                            cancel).ConfigureAwait(false);

                        SetParameters(initializeAckBody.Parameters);
                        break;

                    case FrameType.Version:
                        VersionBody versionBody = await ReadFrameAsync(
                            dataSize,
                            (ref SliceDecoder decoder) => new VersionBody(ref decoder),
                            cancel).ConfigureAwait(false);

                        // We currently only support V1
                        throw new InvalidDataException(
                            $"unsupported Slic versions '{string.Join(", ", versionBody.Versions)}'");

                    default:
                        throw new InvalidDataException($"unexpected Slic frame '{type}'");
                }
            }

            // Start a task to read frames from the network connection.
            _readCompletedSemaphore = new AsyncSemaphore(0, 1);
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await ReadFramesAsync(_readCancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _acceptedStreamQueue.TryComplete(new ConnectionLostException());
                    }
                    catch (Exception exception)
                    {
                        _acceptedStreamQueue.TryComplete(exception);
                    }
                    finally
                    {
                        _readCompletedSemaphore.Release();
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
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;
            }

            // Cancel reading and wait for the reading to complete if reading is in progress.
            _readCancellationTokenSource.Cancel();
            if (_readCompletedSemaphore != null)
            {
                await _readCompletedSemaphore.EnterAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var exception = new ObjectDisposedException($"{typeof(SlicNetworkConnection)}:{this}");

            // Close the network connection.
            await _simpleNetworkConnection.DisposeAsync().ConfigureAwait(false);
            await _simpleNetworkConnectionReader.CompleteAsync(exception).ConfigureAwait(false);
            await _simpleNetworkConnectionWriter.CompleteAsync(exception).ConfigureAwait(false);

            // Unblock requests waiting on the semaphores.
            _bidirectionalStreamSemaphore?.Complete(exception);
            _unidirectionalStreamSemaphore?.Complete(exception);
            _sendSemaphore.Complete(exception);

            foreach (SlicMultiplexedStream stream in _streams.Values)
            {
                stream.AbortRead(SlicStreamError.Aborted.ToError());
                stream.AbortWrite(SlicStreamError.Aborted.ToError());
            }

            _readCancellationTokenSource.Dispose();

            // Unblock task blocked on AcceptStreamAsync
            _acceptedStreamQueue.TryComplete(exception);
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            _simpleNetworkConnection.HasCompatibleParams(remoteEndpoint);

        internal SlicNetworkConnection(
            ISimpleNetworkConnection simpleNetworkConnection,
            bool isServer,
            Func<ISlicFrameReader, ISlicFrameReader> slicFrameReaderDecorator,
            Func<ISlicFrameWriter, ISlicFrameWriter> slicFrameWriterDecorator,
            SlicTransportOptions slicOptions)
        {
            IsServer = isServer;
            PauseWriterThreshold = slicOptions.PauseWriterThreshold;
            ResumeWriterThreshold = slicOptions.ResumeWriterThreshold;
            Pool = slicOptions.Pool;
            MinimumSegmentSize = slicOptions.MinimumSegmentSize;

            _packetMaxSize = slicOptions.PacketMaxSize;
            _bidirectionalMaxStreams = slicOptions.BidirectionalStreamMaxCount;
            _unidirectionalMaxStreams = slicOptions.UnidirectionalStreamMaxCount;
            _simpleNetworkConnection = simpleNetworkConnection;

            _simpleNetworkConnectionWriter = new SimpleNetworkConnectionPipeWriter(
                simpleNetworkConnection,
                slicOptions.Pool,
                slicOptions.MinimumSegmentSize);

            _simpleNetworkConnectionReader = new SimpleNetworkConnectionPipeReader(
                simpleNetworkConnection,
                slicOptions.Pool,
                slicOptions.MinimumSegmentSize);

            var writer = new SlicFrameWriter(_simpleNetworkConnectionWriter);
            var reader = new SlicFrameReader(_simpleNetworkConnectionReader);

            _writer = slicFrameWriterDecorator(writer);
            _reader = slicFrameReaderDecorator(reader);

            // Initially set the peer packet max size to the local max size to ensure we can receive the first
            // initialize frame.
            PeerPacketMaxSize = _packetMaxSize;
            PeerPauseWriterThreshold = PauseWriterThreshold;

            // We use the same stream ID numbering scheme as Quic.
            if (IsServer)
            {
                _nextBidirectionalId = 1;
                _nextUnidirectionalId = 3;
            }
            else
            {
                _nextBidirectionalId = 0;
                _nextUnidirectionalId = 2;
            }
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
        }

        internal async ValueTask SendFrameAsync(
            SlicMultiplexedStream? stream,
            FrameType frameType,
            EncodeAction? encode,
            CancellationToken cancel)
        {
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            try
            {
                await _writer.WriteFrameAsync(frameType, stream?.Id, encode, cancel).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        internal async ValueTask<FlushResult> SendStreamFrameAsync(
            SlicMultiplexedStream stream,
            ReadOnlySequence<byte> source1,
            ReadOnlySequence<byte> source2,
            bool completeWhenDone,
            CancellationToken cancel)
        {
            Debug.Assert(!source1.IsEmpty || completeWhenDone);
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

                // First, if the stream isn't started, we need to acquire the stream count semaphore. If there are more
                // streams opened than the peer allows, this will block until a stream is shutdown.
                if (!stream.IsStarted)
                {
                    AsyncSemaphore streamCountSemaphore = stream.IsBidirectional ?
                        _bidirectionalStreamSemaphore! :
                        _unidirectionalStreamSemaphore!;
                    await streamCountSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                }

                // Next, ensure send credit is available. If not, this will block until the receiver allows sending
                // additional data.
                int sendCredit = await stream.AcquireSendCreditAsync(cancel).ConfigureAwait(false);
                Debug.Assert(sendCredit > 0);

                // Finally, acquire the send semaphore to ensure only one stream writes to the connection.
                await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                try
                {
                    // Allocate stream ID if the stream isn't started. Thread-safety is provided by the send
                    // semaphore.
                    if (!stream.IsStarted)
                    {
                        if (stream.IsBidirectional)
                        {
                            AddStream(_nextBidirectionalId, stream);
                            _nextBidirectionalId += 4;
                        }
                        else
                        {
                            AddStream(_nextUnidirectionalId, stream);
                            _nextUnidirectionalId += 4;
                        }
                    }

                    // Gather data from source1 or source2 up to sendCredit bytes or the Slic packet maximum size.
                    int sendMaxSize = Math.Min(sendCredit, PeerPacketMaxSize);
                    ReadOnlySequence<byte> sendSource1;
                    ReadOnlySequence<byte> sendSource2;
                    if (!source1.IsEmpty)
                    {
                        int length = Math.Min((int)source1.Length, sendMaxSize);
                        sendSource1 = source1.Slice(0, length);
                        source1 = source1.Slice(length);
                    }
                    else
                    {
                        sendSource1 = ReadOnlySequence<byte>.Empty;
                    }

                    if (source1.IsEmpty && !source2.IsEmpty)
                    {
                        int length = Math.Min((int)source2.Length, sendMaxSize - (int)sendSource1.Length);
                        sendSource2 = source2.Slice(0, length);
                        source2 = source2.Slice(length);
                    }
                    else
                    {
                        sendSource2 = ReadOnlySequence<byte>.Empty;
                    }

                    // If there's no data left to send and completeWhenDone is true, send the last stream frame.
                    bool endStream = completeWhenDone && source1.IsEmpty && source2.IsEmpty;

                    // Notify the stream that we're consuming sendSize credit. It's important to call this before
                    // sending the stream frame to avoid race conditions where the StreamConsumed frame could be
                    // received before the send credit was updated.
                    stream.ConsumeSendCredit((int)(sendSource1.Length + sendSource2.Length));

                    if (endStream)
                    {
                        // At this point writes are considered completed on the stream. It's important to call
                        // this before sending the last packet to avoid a race condition where the peer could
                        // start a new stream before the Slic connection stream count is decreased.
                        stream.TrySetWriteCompleted();
                    }

                    // Write the stream frame.
                    await _writer.WriteStreamFrameAsync(
                        stream.Id,
                        sendSource1,
                        sendSource2,
                        endStream,
                        cancel).ConfigureAwait(false);
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
                finally
                {
                    _sendSemaphore.Release();
                }
            }
            while (!source1.IsEmpty || !source2.IsEmpty); // Loop until there's no data left to send.

            return new FlushResult(isCanceled: false, isCompleted: false);
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
                if (type < FrameType.Stream)
                {
                    throw new InvalidDataException($"unexpected Slic frame with frame type '{type}'");
                }
                Debug.Assert(streamId != null);

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

                        int readSize = 0;
                        if (_streams.TryGetValue(streamId.Value, out SlicMultiplexedStream? stream))
                        {
                            // Let the stream receive the data.
                            readSize = await stream.ReceivedStreamFrameAsync(
                                dataSize,
                                endStream,
                                cancel).ConfigureAwait(false);
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

                            // Accept the new remote stream.
                            // TODO: Cache SliceMultiplexedStream
                            stream = new SlicMultiplexedStream(this, isBidirectional, remote: true, _reader, _writer);
                            try
                            {
                                AddStream(streamId.Value, stream);
                            }
                            catch
                            {
                                await stream.Input.CompleteAsync().ConfigureAwait(false);
                                if (isBidirectional)
                                {
                                    await stream.Output.CompleteAsync().ConfigureAwait(false);
                                }
                                Debug.Assert(stream.IsShutdown);
                                throw;
                            }

                            // Let the stream receive the data.
                            readSize = await stream.ReceivedStreamFrameAsync(
                                dataSize,
                                endStream,
                                cancel).ConfigureAwait(false);

                            // Queue the new stream only if it read the full size (otherwise, it has been shutdown).
                            if (readSize == dataSize)
                            {
                                _acceptedStreamQueue.Enqueue(stream);
                            }
                        }

                        if (readSize < dataSize)
                        {
                            // The stream has been shutdown. Read and ignore the data using a helper pipe.
                            var pipe = new Pipe(
                                new PipeOptions(
                                    pool: Pool,
                                    pauseWriterThreshold: 0,
                                    minimumSegmentSize: MinimumSegmentSize,
                                    writerScheduler: PipeScheduler.Inline));

                            await _reader.PipeReader.FillBufferWriterAsync(
                                    pipe.Writer,
                                    dataSize - readSize,
                                    cancel).ConfigureAwait(false);

                            await pipe.Writer.CompleteAsync().ConfigureAwait(false);
                            await pipe.Reader.CompleteAsync().ConfigureAwait(false);
                        }

                        break;
                    }
                    case FrameType.StreamConsumed:
                    {
                        if (dataSize == 0)
                        {
                            throw new InvalidDataException("stream consumed frame too small");
                        }
                        else if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream consumed frame too large");
                        }

                        StreamConsumedBody consumed = await ReadFrameAsync(
                            dataSize,
                            (ref SliceDecoder decoder) => new StreamConsumedBody(ref decoder),
                            cancel).ConfigureAwait(false);
                        if (_streams.TryGetValue(streamId.Value, out SlicMultiplexedStream? stream))
                        {
                            stream.ReceivedConsumedFrame((int)consumed.Size);
                        }
                        break;
                    }
                    case FrameType.StreamReset:
                    {
                        if (dataSize == 0)
                        {
                            throw new InvalidDataException("stream reset frame too small");
                        }
                        else if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream reset frame too large");
                        }

                        StreamResetBody streamReset = await ReadFrameAsync(
                            dataSize,
                            (ref SliceDecoder decoder) => new StreamResetBody(ref decoder),
                            cancel).ConfigureAwait(false);
                        if (_streams.TryGetValue(streamId.Value, out SlicMultiplexedStream? stream))
                        {
                            stream.ReceivedResetFrame(streamReset.ApplicationProtocolErrorCode);
                        }
                        break;
                    }
                    case FrameType.StreamStopSending:
                    {
                        if (dataSize == 0)
                        {
                            throw new InvalidDataException("stream stop sending frame too small");
                        }
                        else if (dataSize > 8)
                        {
                            throw new InvalidDataException("stream stop sending frame too large");
                        }

                        StreamStopSendingBody streamStopSending = await ReadFrameAsync(
                            dataSize,
                            (ref SliceDecoder decoder) => new StreamStopSendingBody(ref decoder),
                            cancel).ConfigureAwait(false);
                        if (_streams.TryGetValue(streamId.Value, out SlicMultiplexedStream? stream))
                        {
                            stream.ReceivedStopSendingFrame(streamStopSending.ApplicationProtocolErrorCode);
                        }
                        break;
                    }
                    case FrameType.UnidirectionalStreamReleased:
                    {
                        if (dataSize > 0)
                        {
                            throw new InvalidDataException("unidirectional stream released frame too large");
                        }

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

        private async ValueTask<T> ReadFrameAsync<T>(
            int size,
            DecodeFunc<T> decodeFunc,
            CancellationToken cancel)
        {
            Debug.Assert(size > 0);
            ReadResult readResult = await _reader.PipeReader.ReadAtLeastAsync(size, cancel).ConfigureAwait(false);

            ReadOnlySequence<byte> buffer = readResult.Buffer;
            if (buffer.Length > size)
            {
                buffer = buffer.Slice(0, size);
            }
            else if (buffer.Length < size)
            {
                throw new ConnectionLostException();
            }

            T decodedFrame = Encoding.Slice20.DecodeBuffer(buffer, decodeFunc);
            if (size > 0)
            {
                _reader.PipeReader.AdvanceTo(buffer.End);
            }

            return decodedFrame;
        }

        private void SetParameters(IDictionary<int, IList<byte>> parameters)
        {
            TimeSpan? peerIdleTimeout = null;

            foreach ((ParameterKey key, ulong value) in parameters.DecodedParameters())
            {
                if (key == ParameterKey.MaxBidirectionalStreams)
                {
                    _bidirectionalStreamSemaphore = new AsyncSemaphore((int)value, (int)value);
                }
                else if (key == ParameterKey.MaxUnidirectionalStreams)
                {
                    _unidirectionalStreamSemaphore = new AsyncSemaphore((int)value, (int)value);
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
