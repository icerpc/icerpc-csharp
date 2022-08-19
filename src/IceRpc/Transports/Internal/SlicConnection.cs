// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>The Slic connection implements an <see cref="IMultiplexedConnection"/> on top of a <see
/// cref="IDuplexConnection"/>.</summary>
internal class SlicConnection : IMultiplexedConnection
{
    public ServerAddress ServerAddress => _duplexConnection.ServerAddress;

    internal bool IsServer { get; }

    internal int MinSegmentSize { get; }

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
    private readonly IDuplexConnection _duplexConnection;
    private readonly DuplexConnectionReader _duplexConnectionReader;
    private readonly DuplexConnectionWriter _duplexConnectionWriter;
    private Exception? _exception;
    private readonly TimeSpan _localIdleTimeout;
    private ulong? _lastRemoteBidirectionalStreamId;
    private ulong? _lastRemoteUnidirectionalStreamId;
    private readonly int _maxBidirectionalStreams;
    private readonly int _maxUnidirectionalStreams;
    // _mutex ensure the assignment of _lastRemoteXxx members and the addition of the stream to _streams is
    // an atomic operation.
    private readonly object _mutex = new();
    private ulong _nextBidirectionalId;
    private ulong _nextUnidirectionalId;
    private readonly int _packetMaxSize;
    private Task? _readFramesTask;
    private readonly ConcurrentDictionary<ulong, SlicStream> _streams = new();
    private readonly CancellationTokenSource _tasksCts = new();
    private int _unidirectionalStreamCount;
    private AsyncSemaphore? _unidirectionalStreamSemaphore;
    private readonly AsyncSemaphore _writeSemaphore = new(1, 1);

    public ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel) =>
        _acceptStreamQueue.DequeueAsync(cancel);

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel)
    {
        // Connect the duplex connection.
        TransportConnectionInformation information = await _duplexConnection.ConnectAsync(
            cancel).ConfigureAwait(false);

        // Enable the idle timeout check after the transport connection establishment. We don't want the transport
        // connection to be disposed because it's idle when the transport connection establishment is in progress. This
        // would require the duplex connection ConnectAsync/Dispose implementations to be thread safe. The transport
        // connection establishment timeout is handled by the cancellation token instead.
        _duplexConnectionReader.EnableIdleCheck();

        TimeSpan peerIdleTimeout = TimeSpan.MaxValue;
        (FrameType FrameType, int FrameSize, ulong?)? header;

        // Initialize the Slic connection.
        if (IsServer)
        {
            // Read the Initialize frame sent by the client.
            header = await ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

            if (header is null || header.Value.FrameSize == 0)
            {
                throw new InvalidDataException("invalid empty initialize frame");
            }

            (ulong version, InitializeBody? initializeBody) = await ReadFrameAsync(
                header.Value.FrameSize,
                DecodeInitialize,
                cancel).ConfigureAwait(false);

            if (version != 1)
            {
                // Unsupported version, try to negotiate another version by sending a Version frame with
                // the Slic versions supported by this server.
                await SendFrameAsync(
                    stream: null,
                    FrameType.Version,
                    new VersionBody(new ulong[] { SlicDefinitions.V1 }).Encode,
                    cancel).ConfigureAwait(false);

                // Read again the Initialize frame sent by the client.
                header = await ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

                if (header is null || header.Value.FrameSize == 0)
                {
                    throw new InvalidDataException("invalid empty initialize frame");
                }

                (version, initializeBody) = await ReadFrameAsync(
                    header.Value.FrameSize,
                    DecodeInitialize,
                    cancel).ConfigureAwait(false);
            }

            if (initializeBody is null)
            {
                throw new InvalidDataException($"unsupported Slic version '{version}'");
            }

            // Check the application protocol and set the parameters.
            string protocolName = initializeBody.Value.ApplicationProtocolName;
            if (!Protocol.TryParse(protocolName, out Protocol? protocol) || protocol != Protocol.IceRpc)
            {
                throw new NotSupportedException($"application protocol '{protocolName}' is not supported");
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
                    encoder.EncodeVarUInt62(SlicDefinitions.V1);
                    initializeBody.Encode(ref encoder);
                },
                cancel).ConfigureAwait(false);

            // Read back either the InitializeAck or Version frame.
            header = await ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

            if (header is null || header.Value.FrameSize == 0)
            {
                throw new InvalidDataException("invalid empty initialize ack frame");
            }

            switch (header.Value.FrameType)
            {
                case FrameType.InitializeAck:
                    InitializeAckBody initializeAckBody = await ReadFrameAsync(
                        header.Value.FrameSize,
                        (ref SliceDecoder decoder) => new InitializeAckBody(ref decoder),
                        cancel).ConfigureAwait(false);

                    SetParameters(initializeAckBody.Parameters);
                    break;

                case FrameType.Version:
                    VersionBody versionBody = await ReadFrameAsync(
                        header.Value.FrameSize,
                        (ref SliceDecoder decoder) => new VersionBody(ref decoder),
                        cancel).ConfigureAwait(false);

                    // We currently only support V1
                    throw new InvalidDataException(
                        $"unsupported Slic versions '{string.Join(", ", versionBody.Versions)}'");

                default:
                    throw new InvalidDataException($"unexpected Slic frame '{header.Value.FrameType}'");
            }
        }

        // Start a task to read frames from the transport connection.
        _readFramesTask = Task.Run(
            async () =>
            {
                try
                {
                    // Read frames. This will return when the Close frame is received.
                    await ReadFramesAsync(_tasksCts.Token).ConfigureAwait(false);

                    var exception = new ConnectionClosedException("transport connection closed by peer");
                    if (Abort(exception))
                    {
                        // Shutdown the duplex connection. This acknowledge the receive of the Close frame and triggers
                        // the completion of the peer's ReadFramesAsync call.
                        await _duplexConnection.ShutdownAsync(cancel).ConfigureAwait(false);

                        // Time for AcceptStreamAsync to return.
                        _acceptStreamQueue.TryComplete(exception);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Nothing to do, DisposeAsync has been called and it takes care of the cleanup.
                }
                catch (Exception ex)
                {
                    // Unexpected transport exception.
                    var exception = new ConnectionLostException(ex);
                    if (Abort(exception))
                    {
                        _acceptStreamQueue.TryComplete(exception);
                    }
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
        new SlicStream(this, bidirectional, remote: false);

    public ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            _disposeTask ??= PerformDisposeAsync();
        }
        return new(_disposeTask);

        async Task PerformDisposeAsync()
        {
            Abort(new ConnectionAbortedException("transport connection disposed"));

            // Cancel tasks which are using the transport connection before disposing the transport connection.
            _tasksCts.Cancel();

            await Task.WhenAll(
                _writeSemaphore.CompleteAndWaitAsync(_exception!),
                _readFramesTask ?? Task.CompletedTask).ConfigureAwait(false);

            _acceptStreamQueue.TryComplete(_exception!);

            // Dispose the transport connection and the reader/writer.
            _duplexConnection.Dispose();
            _duplexConnectionReader.Dispose();
            _duplexConnectionWriter.Dispose();

            _tasksCts.Dispose();
        }
    }

    public async Task ShutdownAsync(Exception exception, CancellationToken cancel)
    {
        if (Abort(exception))
        {
            // Wait for writes to complete and send the close frame.
            await _writeSemaphore.CompleteAndWaitAsync(exception).WaitAsync(cancel).ConfigureAwait(false);

            // Send the close frame.
            await WriteFrameAsync(
                FrameType.Close,
                streamId: null,
                new CloseBody(0).Encode, // There's no need for an error code at this point, so we use 0.
                cancel).ConfigureAwait(false);

            // Shutdown the duplex connection.
            await _duplexConnection.ShutdownAsync(cancel).ConfigureAwait(false);
        }
    }

    internal SlicConnection(
        IDuplexConnection duplexConnection,
        MultiplexedConnectionOptions options,
        SlicTransportOptions slicOptions,
        bool isServer)
    {
        if (options.StreamErrorCodeConverter is null)
        {
            throw new ArgumentException(nameof(options), $"{nameof(options.StreamErrorCodeConverter)} is null");
        }

        IsServer = isServer;
        ErrorCodeConverter = options.StreamErrorCodeConverter;

        Pool = options.Pool;
        MinSegmentSize = options.MinSegmentSize;
        _maxBidirectionalStreams = options.MaxBidirectionalStreams;
        _maxUnidirectionalStreams = options.MaxUnidirectionalStreams;

        PauseWriterThreshold = slicOptions.PauseWriterThreshold;
        ResumeWriterThreshold = slicOptions.ResumeWriterThreshold;
        _localIdleTimeout = slicOptions.IdleTimeout;
        _packetMaxSize = slicOptions.PacketMaxSize;

        _duplexConnection = duplexConnection;

        _duplexConnectionWriter = new DuplexConnectionWriter(
            duplexConnection,
            options.Pool,
            options.MinSegmentSize);

        Action? keepAliveAction = null;
        if (!IsServer)
        {
            // Only client connections send ping frames when idle to keep the connection alive.
            keepAliveAction = () => SendFrameAsync(stream: null, FrameType.Ping, null, default).AsTask();
        }

        _duplexConnectionReader = new DuplexConnectionReader(
            duplexConnection,
            idleTimeout: _localIdleTimeout,
            options.Pool,
            options.MinSegmentSize,
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

    internal ValueTask FillBufferWriterAsync(
        IBufferWriter<byte> bufferWriter,
        int byteCount,
        CancellationToken cancel) =>
        _duplexConnectionReader.FillBufferWriterAsync(bufferWriter, byteCount, cancel);

    internal void ReleaseStream(SlicStream stream)
    {
        Debug.Assert(stream.IsStarted);

        _streams.TryRemove(stream.Id, out SlicStream? _);

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
        SlicStream? stream,
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
        SlicStream stream,
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
                // Allocate stream ID if the stream isn't started. Thread-safety is provided by the write semaphore.
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
                    stream.TrySetWritesClosed(exception: null);
                }

                // Write the stream frame.
                await WriteStreamFrameAsync(
                    stream.Id,
                    sendSource1,
                    sendSource2,
                    lastStreamFrame,
                    _tasksCts.Token).AsTask().WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_tasksCts.IsCancellationRequested)
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

    private bool Abort(Exception exception)
    {
        lock (_mutex)
        {
            if (_exception is not null)
            {
                return false;
            }
            _exception = exception;
        }

        foreach (SlicStream stream in _streams.Values)
        {
            stream.Abort(exception);
        }

        // Unblock requests waiting on the semaphores.
        _bidirectionalStreamSemaphore?.Complete(exception);
        _unidirectionalStreamSemaphore?.Complete(exception);
        _writeSemaphore.Complete(exception);

        return true;
    }

    private void AddStream(ulong id, SlicStream stream)
    {
        lock (_mutex)
        {
            if (_exception is not null)
            {
                throw ExceptionUtil.Throw(_exception);
            }

            _streams[id] = stream;

            // Assign the stream ID within the mutex to ensure that the addition of the stream to the connection and the
            // stream ID assignment are atomic.
            stream.Id = id;

            // Keep track of the last assigned stream ID. This is used to figure out if the stream is known or unknown.
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

    private Dictionary<ParameterKey, IList<byte>> GetParameters()
    {
        var parameters = new List<KeyValuePair<ParameterKey, IList<byte>>>
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
        return new Dictionary<ParameterKey, IList<byte>>(parameters);

        static KeyValuePair<ParameterKey, IList<byte>> EncodeParameter(ParameterKey key, ulong value)
        {
            int sizeLength = SliceEncoder.GetVarUInt62EncodedSize(value);
            byte[] buffer = new byte[sizeLength];
            SliceEncoder.EncodeVarUInt62(value, buffer);
            return new(key, buffer);
        }
    }

    private async ValueTask<T> ReadFrameAsync<T>(
        int size,
        DecodeFunc<T> decodeFunc,
        CancellationToken cancel)
    {
        Debug.Assert(size > 0);

        ReadOnlySequence<byte> buffer = await _duplexConnectionReader.ReadAtLeastAsync(
            size, cancel).ConfigureAwait(false);

        if (buffer.Length > size)
        {
            buffer = buffer.Slice(0, size);
        }

        T decodedFrame = SliceEncoding.Slice2.DecodeBuffer(buffer, decodeFunc);
        _duplexConnectionReader.AdvanceTo(buffer.End);
        return decodedFrame;
    }

    private async ValueTask<(FrameType FrameType, int FrameSize, ulong? StreamId)?> ReadFrameHeaderAsync(
        CancellationToken cancel)
    {
        while (true)
        {
            // Read data from the pipe reader.
            if (!_duplexConnectionReader.TryRead(out ReadOnlySequence<byte> buffer))
            {
                buffer = await _duplexConnectionReader.ReadAsync(cancel).ConfigureAwait(false);
            }

            if (buffer.IsEmpty)
            {
                return null; // Peer shutdown the duplex connection.
            }

            if (TryDecodeHeader(
                buffer,
                out (FrameType FrameType, int FrameSize, ulong? StreamId) header,
                out int consumed))
            {
                _duplexConnectionReader.AdvanceTo(buffer.GetPosition(consumed));
                return header;
            }
            else
            {
                _duplexConnectionReader.AdvanceTo(buffer.Start, buffer.End);
            }
        }

        static bool TryDecodeHeader(
            ReadOnlySequence<byte> buffer,
            out (FrameType FrameType, int FrameSize, ulong? StreamId) header,
            out int consumed)
        {
            header = default;
            consumed = default;

            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);

            // Decode the frame type and frame size.
            if (!decoder.TryDecodeVarUInt62(out ulong frameType) ||
                !decoder.TryDecodeSize(out header.FrameSize))
            {
                return false;
            }
            header.FrameType = frameType.AsFrameType();

            // If it's a stream frame, try to decode the stream ID
            if (header.FrameType >= FrameType.Stream)
            {
                consumed = (int)decoder.Consumed;
                if (!decoder.TryDecodeVarUInt62(out ulong streamId))
                {
                    return false;
                }
                header.StreamId = streamId;
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
            (FrameType, int, ulong?)? header = await ReadFrameHeaderAsync(cancel).ConfigureAwait(false);
            if (header is null)
            {
                lock (_mutex)
                {
                    if (_exception is not null)
                    {
                        // Expected if shutting down.
                        return;
                    }
                    else
                    {
                        throw new InvalidDataException("unexpected peer connection shutdown");
                    }
                }
            }

            (FrameType type, int dataSize, ulong? streamId) = header.Value;

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
                    bool isRemote = streamId % 2 == (IsServer ? 0ul : 1ul);
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
                    if (_streams.TryGetValue(streamId.Value, out SlicStream? stream))
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
                        stream = new SlicStream(this, isBidirectional, remote: true);

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
                                minimumSegmentSize: MinSegmentSize,
                                writerScheduler: PipeScheduler.Inline));

                        await _duplexConnectionReader.FillBufferWriterAsync(
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
                    if (_streams.TryGetValue(streamId.Value, out SlicStream? stream))
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
                    if (_streams.TryGetValue(streamId.Value, out SlicStream? stream))
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
                    if (_streams.TryGetValue(streamId.Value, out SlicStream? stream))
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

        bool IsKnownRemoteStream(ulong streamId, bool bidirectional)
        {
            if (bidirectional)
            {
                return _lastRemoteBidirectionalStreamId is not null && streamId <= _lastRemoteBidirectionalStreamId;
            }
            else
            {
                return _lastRemoteUnidirectionalStreamId is not null && streamId <= _lastRemoteUnidirectionalStreamId;
            }
        }
    }

    private void SetParameters(IDictionary<ParameterKey, IList<byte>> parameters)
    {
        TimeSpan? peerIdleTimeout = null;

        IEnumerable<(ParameterKey Key, ulong Value)> decodedParameters =
            parameters.Select(pair =>
                (pair.Key,
                 SliceEncoding.Slice2.DecodeBuffer(
                     new ReadOnlySequence<byte>(pair.Value.ToArray()), // TODO: fix to avoid copy
                     (ref SliceDecoder decoder) => decoder.DecodeVarUInt62())));

        foreach ((ParameterKey key, ulong value) in decodedParameters)
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
                // Ignore unsupported parameter.
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
            _duplexConnectionReader.EnableIdleCheck(peerIdleTimeoutValue);
        }
    }

    private ValueTask WriteFrameAsync(
        FrameType frameType,
        ulong? streamId,
        EncodeAction? encode,
        CancellationToken cancel)
    {
        var encoder = new SliceEncoder(_duplexConnectionWriter, SliceEncoding.Slice2);
        encoder.EncodeFrameType(frameType);
        Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
        int startPos = encoder.EncodedByteCount;

        if (streamId is not null)
        {
            encoder.EncodeVarUInt62(streamId.Value);
        }
        encode?.Invoke(ref encoder);
        SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);

        return _duplexConnectionWriter.FlushAsync(cancel);
    }

    private ValueTask WriteStreamFrameAsync(
        ulong streamId,
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        CancellationToken cancel)
    {
        var encoder = new SliceEncoder(_duplexConnectionWriter, SliceEncoding.Slice2);
        encoder.EncodeFrameType(endStream ? FrameType.StreamLast : FrameType.Stream);
        Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
        int startPos = encoder.EncodedByteCount;
        encoder.EncodeVarUInt62(streamId);
        SliceEncoder.EncodeVarUInt62(
            (ulong)(encoder.EncodedByteCount - startPos + source1.Length + source2.Length), sizePlaceholder);

        return _duplexConnectionWriter.WriteAsync(source1, source2, cancel);
    }
}
