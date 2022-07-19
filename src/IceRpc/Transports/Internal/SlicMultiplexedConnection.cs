// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>The Slic multiplexed connection implements an <see cref="IMultiplexedConnection"/> on top of a <see
/// cref="IDuplexConnection"/>.</summary>
internal class SlicMultiplexedConnection : IMultiplexedConnection
{
    public Endpoint Endpoint => _transportConnection.Endpoint;

    internal bool IsServer { get; }

    internal int MinimumSegmentSize { get; }

    internal IMultiplexedStreamErrorCodeConverter ErrorCodeConverter { get; }

    internal int PauseWriterThreshold { get; }

    internal int PeerPacketMaxSize { get; private set; }

    internal int PeerPauseWriterThreshold { get; private set; }

    internal MemoryPool<byte> Pool { get; }

    internal int ResumeWriterThreshold { get; }

    private readonly AsyncQueue<IMultiplexedStream> _acceptStreamQueue = new();
    private int _bidirectionalStreamCount;
    private AsyncSemaphore? _bidirectionalStreamSemaphore;
    private Task? _disposeTask;
    private bool _isReadOnly;
    private readonly TimeSpan _localIdleTimeout;
    private long _lastRemoteBidirectionalStreamId = -1;
    private long _lastRemoteUnidirectionalStreamId = -1;
    private readonly int _maxBidirectionalStreams;
    private readonly int _maxUnidirectionalStreams;
    // _mutex ensure the assignment of _lastRemoteXxx members and the addition of the stream to _streams is
    // an atomic operation.
    private readonly object _mutex = new();
    private long _nextBidirectionalId;
    private long _nextUnidirectionalId;
    private readonly int _packetMaxSize;
    private Task? _readFramesTask;
    private readonly ConcurrentDictionary<long, SlicMultiplexedStream> _streams = new();
    private readonly CancellationTokenSource _tasksCancelSource = new();
    private readonly IDuplexConnection _transportConnection;
    private readonly DuplexConnectionReader _transportConnectionReader;
    private readonly DuplexConnectionWriter _transportConnectionWriter;
    private int _unidirectionalStreamCount;
    private AsyncSemaphore? _unidirectionalStreamSemaphore;
    private readonly AsyncSemaphore _writeSemaphore = new(1, 1);

    public ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel) =>
        _acceptStreamQueue.DequeueAsync(cancel);

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel)
    {
        // Connect the duplex connection.
        TransportConnectionInformation information = await _transportConnection.ConnectAsync(
            cancel).ConfigureAwait(false);

        // Enable the idle timeout check after the transport connection establishment. We don't want the transport
        // connection to be disposed because it's idle when the transport connection establishment is in progress. This
        // would require the duplex connection ConnectAsync/Dispose implementations to be thread safe. The transport
        // connection establishment timeout is handled by the cancellation token instead.
        _transportConnectionReader.EnableIdleCheck();

        TimeSpan peerIdleTimeout = TimeSpan.MaxValue;

        // Initialize the Slic connection.
        if (IsServer)
        {
            // Read the Initialize frame sent by the client.
            (FrameType type, int dataSize, _) = await ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

            if (dataSize == 0)
            {
                throw new InvalidDataException("invalid empty initialize frame");
            }

            (uint version, InitializeBody? initializeBody) = await ReadFrameAsync(
                dataSize,
                DecodeInitialize,
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
                (type, dataSize, _) = await ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

                if (dataSize == 0)
                {
                    throw new InvalidDataException("invalid empty initialize frame");
                }

                (version, initializeBody) = await ReadFrameAsync(
                    dataSize,
                    DecodeInitialize,
                    cancel).ConfigureAwait(false);
            }

            if (initializeBody is null)
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
                    encoder.EncodeVarUInt32(SlicDefinitions.V1);
                    initializeBody.Encode(ref encoder);
                },
                cancel).ConfigureAwait(false);

            // Read back either the InitializeAck or Version frame.
            (FrameType type, int dataSize, _) = await ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

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

        // Start a task to read frames from the transport connection.
        _readFramesTask = Task.Run(
            async () =>
            {
                Exception? completeException = null;
                try
                {
                    // Read frames. This will return when the Close frame is received.
                    await ReadFramesAsync(_tasksCancelSource.Token).ConfigureAwait(false);

                    completeException = new ConnectionClosedException("transport connection closed");
                }
                catch (OperationCanceledException)
                {
                    completeException = new ConnectionAbortedException("transport connection disposed");
                }
                catch (ObjectDisposedException)
                {
                    completeException = new ConnectionAbortedException("transport connection disposed");
                }
                catch (Exception ex)
                {
                    completeException = ex;
                }
                finally
                {
                    Debug.Assert(completeException is not null);
                    ShutdownCore(completeException);
                }
            },
            CancellationToken.None);

        return information;

        static (uint, InitializeBody?) DecodeInitialize(ref SliceDecoder decoder)
        {
            uint version = decoder.DecodeVarUInt32();
            if (version == SlicDefinitions.V1)
            {
                return (version, new InitializeBody(ref decoder));
            }
            else
            {
                return (version, null);
            }
        }
    }

    public IMultiplexedStream CreateStream(bool bidirectional) =>
        // TODO: Cache SlicMultiplexedStream
        new SlicMultiplexedStream(this, bidirectional, remote: false);

    public ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            _isReadOnly = true;
            _disposeTask ??= PerformDisposeAsync();
        }
        return new(_disposeTask);

        async Task PerformDisposeAsync()
        {
            var exception = new ConnectionAbortedException("transport connection disposed");

            // Cancel tasks which are using the transport connection before disposing the transport connection.
            _tasksCancelSource.Cancel();
            try
            {
                await Task.WhenAll(
                    _writeSemaphore.CompleteAndWaitAsync(exception),
                    _readFramesTask ?? Task.CompletedTask).ConfigureAwait(false);
            }
            catch
            {
                // Ignore.
            }

            // Dispose the transport connection.
            _transportConnection.Dispose();

            foreach (SlicMultiplexedStream stream in _streams.Values)
            {
                stream.Abort(exception);
            }

            _acceptStreamQueue.TryComplete(exception);
            _bidirectionalStreamSemaphore?.Complete(exception);
            _unidirectionalStreamSemaphore?.Complete(exception);

            // It's now safe to dispose of the reader/writer since no more threads are sending/receiving data.
            _transportConnectionReader.Dispose();
            _transportConnectionWriter.Dispose();

            _tasksCancelSource.Dispose();
        }
    }

    public async Task ShutdownAsync(Exception completeException, CancellationToken cancel)
    {
        ShutdownCore(completeException);

        // Wait for writes to complete and send the close frame.
        await _writeSemaphore.CompleteAndWaitAsync(completeException).WaitAsync(cancel).ConfigureAwait(false);

        // Send the close frame.
        try
        {
            await WriteFrameAsync(
                FrameType.Close,
                streamId: null,
                new CloseBody(0).Encode, // There's no need for an error code at this point, so we use 0.
                cancel).ConfigureAwait(false);

            // Shutdown the duplex connection.
            await _transportConnection.ShutdownAsync(cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Ignore, this can occur if the peer already close the connection.
        }
    }

    internal SlicMultiplexedConnection(
        IDuplexConnection duplexConnection,
        bool isServer,
        MultiplexedConnectionOptions options,
        SlicTransportOptions slicOptions)
    {
        if (options.StreamErrorCodeConverter is null)
        {
            throw new ArgumentException(nameof(options), $"{nameof(options.StreamErrorCodeConverter)} is null");
        }

        IsServer = isServer;
        ErrorCodeConverter = options.StreamErrorCodeConverter;

        Pool = options.Pool;
        MinimumSegmentSize = options.MinimumSegmentSize;
        _maxBidirectionalStreams = options.MaxBidirectionalStreams;
        _maxUnidirectionalStreams = options.MaxUnidirectionalStreams;

        PauseWriterThreshold = slicOptions.PauseWriterThreshold;
        ResumeWriterThreshold = slicOptions.ResumeWriterThreshold;
        _localIdleTimeout = slicOptions.IdleTimeout;
        _packetMaxSize = slicOptions.PacketMaxSize;

        _transportConnection = duplexConnection;

        _transportConnectionWriter = new DuplexConnectionWriter(
            duplexConnection,
            options.Pool,
            options.MinimumSegmentSize);

        Action? keepAliveAction = null;
        if (!IsServer)
        {
            // Only client connections send ping frames when idle to keep the connection alive.
            keepAliveAction = () => SendFrameAsync(stream: null, FrameType.Ping, null, default).AsTask();
        }

        _transportConnectionReader = new DuplexConnectionReader(
            duplexConnection,
            idleTimeout: _localIdleTimeout,
            options.Pool,
            options.MinimumSegmentSize,
            abortAction: exception => _acceptStreamQueue.TryComplete(exception),
            keepAliveAction);

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
            if (_isReadOnly)
            {
                throw new ConnectionAbortedException("transport connection disposed");
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

    internal ValueTask FillBufferWriterAsync(
        IBufferWriter<byte> bufferWriter,
        int byteCount,
        CancellationToken cancel) =>
        _transportConnectionReader.FillBufferWriterAsync(bufferWriter, byteCount, cancel);

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
        await _writeSemaphore.EnterAsync(cancel).ConfigureAwait(false);
        try
        {
            await WriteFrameAsync(
                frameType,
                stream?.Id,
                encode,
                cancel).ConfigureAwait(false);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    internal async ValueTask<FlushResult> SendStreamFrameAsync(
        SlicMultiplexedStream stream,
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        CancellationToken cancel)
    {
        Debug.Assert(!source1.IsEmpty || endStream);
        if (_bidirectionalStreamSemaphore is null)
        {
            throw new InvalidOperationException("cannot send a stream before calling ConnectAsync");
        }

        do
        {
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

            // Finally, acquire the write semaphore to ensure only one stream writes to the connection.
            await _writeSemaphore.EnterAsync(cancel).ConfigureAwait(false);
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

                // If there's no data left to send and endStream is true, it's the last stream frame.
                bool lastStreamFrame = endStream && source1.IsEmpty && source2.IsEmpty;

                // Notify the stream that we're consuming sendSize credit. It's important to call this before
                // sending the stream frame to avoid race conditions where the StreamConsumed frame could be
                // received before the send credit was updated.
                stream.ConsumeSendCredit((int)(sendSource1.Length + sendSource2.Length));

                if (lastStreamFrame)
                {
                    // At this point writes are considered completed on the stream. It's important to call this
                    // before sending the last packet to avoid a race condition where the peer could start a new
                    // stream before the Slic connection stream count is decreased.
                    stream.TrySetWriteCompleted();
                }

                // Write the stream frame.
                await WriteStreamFrameAsync(
                    stream.Id,
                    sendSource1,
                    sendSource2,
                    lastStreamFrame,
                    _tasksCancelSource.Token).AsTask().WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_tasksCancelSource.IsCancellationRequested)
            {
                throw new ConnectionAbortedException("transport connection disposed");
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }
        while (!source1.IsEmpty || !source2.IsEmpty); // Loop until there's no data left to send.

        return new FlushResult(isCanceled: false, isCompleted: false);
    }

    private Dictionary<int, IList<byte>> GetParameters()
    {
        var parameters = new List<KeyValuePair<int, IList<byte>>>
        {
            EncodeParameter(ParameterKey.MaxBidirectionalStreams, (ulong)_maxBidirectionalStreams),
            EncodeParameter(ParameterKey.MaxUnidirectionalStreams, (ulong)_maxUnidirectionalStreams),
            EncodeParameter(ParameterKey.PacketMaxSize, (ulong)_packetMaxSize),
            EncodeParameter(ParameterKey.PauseWriterThreshold, (ulong)PauseWriterThreshold)
        };

        if (_localIdleTimeout != Timeout.InfiniteTimeSpan)
        {
            parameters.Add(EncodeParameter(ParameterKey.IdleTimeout, (ulong)_localIdleTimeout.TotalMilliseconds));
        }
        return new Dictionary<int, IList<byte>>(parameters);

        static KeyValuePair<int, IList<byte>> EncodeParameter(ParameterKey key, ulong value)
        {
            int sizeLength = SliceEncoder.GetVarUInt62EncodedSize(value);
            byte[] buffer = new byte[sizeLength];
            SliceEncoder.EncodeVarUInt62(value, buffer);
            return new((int)key, buffer);
        }
    }

    private async ValueTask<T> ReadFrameAsync<T>(
        int size,
        DecodeFunc<T> decodeFunc,
        CancellationToken cancel)
    {
        Debug.Assert(size > 0);

        ReadOnlySequence<byte> buffer = await _transportConnectionReader.ReadAtLeastAsync(
            size, cancel).ConfigureAwait(false);

        if (buffer.Length > size)
        {
            buffer = buffer.Slice(0, size);
        }

        T decodedFrame = SliceEncoding.Slice2.DecodeBuffer(buffer, decodeFunc);
        _transportConnectionReader.AdvanceTo(buffer.End);
        return decodedFrame;
    }

    private async ValueTask<(FrameType FrameType, int FrameSize, long? StreamId)> ReadFrameHeaderAsync(
        CancellationToken cancel)
    {
        while (true)
        {
            // Read data from the pipe reader.
            if (!_transportConnectionReader.TryRead(out ReadOnlySequence<byte> buffer))
            {
                buffer = await _transportConnectionReader.ReadAsync(cancel).ConfigureAwait(false);
            }

            if (TryDecodeHeader(
                buffer,
                out (FrameType FrameType, int FrameSize, long? StreamId) header,
                out int consumed))
            {
                _transportConnectionReader.AdvanceTo(buffer.GetPosition(consumed));
                return header;
            }
            else
            {
                _transportConnectionReader.AdvanceTo(buffer.Start, buffer.End);
            }
        }

        static bool TryDecodeHeader(
            ReadOnlySequence<byte> buffer,
            out (FrameType FrameType, int FrameSize, long? StreamId) header,
            out int consumed)
        {
            header = default;
            consumed = default;

            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);

            // Decode the frame type and frame size.
            if (!decoder.TryDecodeUInt8(out byte frameType) ||
                !decoder.TryDecodeSize(out header.FrameSize))
            {
                return false;
            }
            header.FrameType = (FrameType)frameType;

            // If it's a stream frame, try to decode the stream ID
            if (header.FrameType >= FrameType.Stream)
            {
                consumed = (int)decoder.Consumed;
                if (!decoder.TryDecodeVarUInt62(out ulong streamId))
                {
                    return false;
                }
                header.StreamId = (long)streamId;
                header.FrameSize -= (int)decoder.Consumed - consumed;
            }

            consumed = (int)decoder.Consumed;
            return true;
        }
    }

    private async Task ReadFramesAsync(CancellationToken cancel)
    {
        while (true)
        {
            (FrameType type, int dataSize, long? streamId) =
                await ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

            // Only stream frames are expected at this point. Non stream frames are only exchanged at the
            // initialization step.
            if (type < FrameType.Close)
            {
                throw new InvalidDataException($"unexpected Slic frame with frame type '{type}'");
            }

            switch (type)
            {
                case FrameType.Close:
                {
                    CloseBody closeBody = await ReadFrameAsync(
                        dataSize,
                        (ref SliceDecoder decoder) => new CloseBody(ref decoder),
                        cancel).ConfigureAwait(false);

                    // Graceful connection shutdown, we're done.
                    return;
                }
                case FrameType.Ping:
                {
                    // Send back a pong frame to let the peer know that we're still alive.
                    ValueTask _ = SendFrameAsync(stream: null, FrameType.Pong, null, default);
                    break;
                }
                case FrameType.Pong:
                {
                    // Nothing to do, the duplex connection reader keeps track of the last activity time.
                    break;
                }
                case FrameType.Stream:
                case FrameType.StreamLast:
                {
                    Debug.Assert(streamId is not null);
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
                            if (_bidirectionalStreamCount == _maxBidirectionalStreams)
                            {
                                throw new InvalidDataException(
                                    $"maximum bidirectional stream count {_maxBidirectionalStreams} reached");
                            }
                            Interlocked.Increment(ref _bidirectionalStreamCount);
                        }
                        else
                        {
                            if (_unidirectionalStreamCount == _maxUnidirectionalStreams)
                            {
                                throw new InvalidDataException(
                                    $"maximum unidirectional stream count {_maxUnidirectionalStreams} reached");
                            }
                            Interlocked.Increment(ref _unidirectionalStreamCount);
                        }

                        // Accept the new remote stream.
                        // TODO: Cache SliceMultiplexedStream
                        stream = new SlicMultiplexedStream(this, isBidirectional, remote: true);

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
                            _acceptStreamQueue.Enqueue(stream);
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

                        await _transportConnectionReader.FillBufferWriterAsync(
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
                    Debug.Assert(streamId is not null);
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
                    Debug.Assert(streamId is not null);
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
                    Debug.Assert(streamId is not null);
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
                    Debug.Assert(streamId is not null);
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

        if (_bidirectionalStreamSemaphore is null)
        {
            throw new InvalidDataException("missing MaxBidirectionalStreams Slic connection parameter");
        }

        if (_unidirectionalStreamSemaphore is null)
        {
            throw new InvalidDataException("missing MaxUnidirectionalStreams Slic connection parameter");
        }

        if (PeerPacketMaxSize < 1024)
        {
            throw new InvalidDataException($"invalid PacketMaxSize={PeerPacketMaxSize} Slic connection parameter");
        }

        // Use the smallest idle timeout.
        if (peerIdleTimeout is TimeSpan peerIdleTimeoutValue && peerIdleTimeoutValue < _localIdleTimeout)
        {
            _transportConnectionReader.EnableIdleCheck(peerIdleTimeoutValue);
        }
    }

    private void ShutdownCore(Exception completeException)
    {
        lock (_mutex)
        {
            _isReadOnly = true;
        }

        foreach (SlicMultiplexedStream stream in _streams.Values)
        {
            stream.Abort(completeException);
        }

        // Unblock requests waiting on the semaphores.
        _bidirectionalStreamSemaphore?.Complete(completeException);
        _unidirectionalStreamSemaphore?.Complete(completeException);
        _acceptStreamQueue.TryComplete(completeException);
        _writeSemaphore.Complete(completeException);
    }

    private ValueTask WriteFrameAsync(
        FrameType frameType,
        long? streamId,
        EncodeAction? encode,
        CancellationToken cancel)
    {
        var encoder = new SliceEncoder(_transportConnectionWriter, SliceEncoding.Slice2);
        encoder.EncodeUInt8((byte)frameType);
        Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
        int startPos = encoder.EncodedByteCount;

        if (streamId is not null)
        {
            encoder.EncodeVarUInt62((ulong)streamId);
        }
        encode?.Invoke(ref encoder);
        SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);

        return _transportConnectionWriter.FlushAsync(cancel);
    }

    private ValueTask WriteStreamFrameAsync(
        long streamId,
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        CancellationToken cancel)
    {
        var encoder = new SliceEncoder(_transportConnectionWriter, SliceEncoding.Slice2);
        encoder.EncodeUInt8((byte)(endStream ? FrameType.StreamLast : FrameType.Stream));
        Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
        int startPos = encoder.EncodedByteCount;
        encoder.EncodeVarUInt62((ulong)streamId);
        SliceEncoder.EncodeVarUInt62(
            (ulong)(encoder.EncodedByteCount - startPos + source1.Length + source2.Length), sizePlaceholder);

        return _transportConnectionWriter.WriteAsync(source1, source2, cancel);
    }
}
