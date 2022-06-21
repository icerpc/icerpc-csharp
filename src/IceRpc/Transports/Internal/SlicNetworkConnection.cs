// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>The Slic multiplexed network connection implements an <see cref="IMultiplexedNetworkConnection"/> on
/// top of a <see cref="ISimpleNetworkConnection"/>.</summary>
internal class SlicNetworkConnection : IMultiplexedNetworkConnection
{
    internal bool IsAborted => _exception != null;

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
    private readonly int _bidirectionalMaxStreams;
    private Exception? _exception;
    private readonly TimeSpan _localIdleTimeout;
    private long _lastRemoteBidirectionalStreamId = -1;
    private long _lastRemoteUnidirectionalStreamId = -1;
    // _mutex ensure the assignment of _lastRemoteXxx members and the addition of the stream to _streams is
    // an atomic operation.
    private readonly object _mutex = new();
    private long _nextBidirectionalId;
    private long _nextUnidirectionalId;
    private readonly int _packetMaxSize;
    private readonly CancellationTokenSource _readCancelSource = new();
    private TaskCompletionSource? _readFramesTaskCompletionSource;
    private readonly ISimpleNetworkConnection _simpleNetworkConnection;
    private readonly SimpleNetworkConnectionReader _simpleNetworkConnectionReader;
    private readonly SimpleNetworkConnectionWriter _simpleNetworkConnectionWriter;
    private readonly ConcurrentDictionary<long, SlicMultiplexedStream> _streams = new();
    private readonly int _unidirectionalMaxStreams;
    private int _unidirectionalStreamCount;
    private AsyncSemaphore? _unidirectionalStreamSemaphore;
    private readonly AsyncSemaphore _writeSemaphore = new(1, 1);

    public void Abort(Exception exception)
    {
        lock (_mutex)
        {
            if (_exception != null)
            {
                return;
            }
            _exception = exception;
        }

        // Close the network connection and cancel the pending receive or shutdown.
        _simpleNetworkConnection.Dispose();
        _readCancelSource.Cancel();

        // Unblock requests waiting on the semaphores.
        _bidirectionalStreamSemaphore?.Complete(_exception);
        _unidirectionalStreamSemaphore?.Complete(_exception);
        _writeSemaphore.Complete(_exception);

        foreach (SlicMultiplexedStream stream in _streams.Values)
        {
            stream.Abort(_exception);
        }

        _acceptStreamQueue.TryComplete(_exception);

        // Release remaining resources in the background.
        _ = AbortCoreAsync();

        async Task AbortCoreAsync()
        {
            Debug.Assert(_exception != null);

            // Unblock requests waiting on the semaphore and wait for the semaphore to be released to ensure it's
            // safe to dispose the simple network connection writer.
            await _writeSemaphore.CompleteAndWaitAsync(_exception).ConfigureAwait(false);

            // Wait for the receive task to complete to ensure we don't dispose the simple network connection reader
            // while it's being used.
            if (_readFramesTaskCompletionSource != null)
            {
                await _readFramesTaskCompletionSource.Task.ConfigureAwait(false);
            }

            // It's now safe to dispose of the reader/writer since no more threads are sending/receiving data.
            _simpleNetworkConnectionReader.Dispose();
            _simpleNetworkConnectionWriter.Dispose();

            _readCancelSource.Dispose();
        }
    }

    public ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel) =>
        _acceptStreamQueue.DequeueAsync(cancel);

    public async Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel)
    {
        Debug.Assert(_readFramesTaskCompletionSource == null); // ConnectAsync should be called only once.

        lock (_mutex)
        {
            if (_exception != null)
            {
                throw ExceptionUtil.Throw(_exception);
            }

            // Resource cleanup needs to know when no more reads are pending to release the simple network connection
            // reader.
            _readFramesTaskCompletionSource = new();
        }

        NetworkConnectionInformation information;
        try
        {
            // Connect the simple network connection.
            information = await _simpleNetworkConnection.ConnectAsync(cancel).ConfigureAwait(false);

            // Set the idle timeout after the network connection establishment. We don't want the network connection to
            // be disposed because it's idle when the network connection establishment is in progress. This would
            // require the simple network connection ConnectAsync/Dispose implementations to be thread safe. The network
            // connection establishment timeout is handled by the cancellation token instead.
            _simpleNetworkConnectionReader.SetIdleTimeout(_localIdleTimeout);

            TimeSpan peerIdleTimeout = TimeSpan.MaxValue;

            // Initialize the Slic connection.
            if (IsServer)
            {
                // Read the Initialize frame sent by the client.
                uint version;
                InitializeBody? initializeBody;
                (FrameType type, int dataSize, _) = await ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

                if (dataSize == 0)
                {
                    throw new InvalidDataException("invalid empty initialize frame");
                }

                (version, initializeBody) = await ReadFrameAsync(
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
        }
        catch (ObjectDisposedException)
        {
            _readFramesTaskCompletionSource.SetResult();

            // The simple network connection can only be disposed if this connection is aborted.
            lock (_mutex)
            {
                Debug.Assert(_exception != null);
                throw ExceptionUtil.Throw(_exception);
            }
        }
        catch
        {
            _readFramesTaskCompletionSource.SetResult();
            throw;
        }

        // Start a task to read frames from the network connection.
        _ = Task.Run(
            async () =>
            {
                Exception? exception = null;
                try
                {
                    // Read frames. This will return when the Close frame is received.
                    await ReadFramesAsync(_readCancelSource.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    Abort(exception ?? new ConnectionClosedException());
                    _readFramesTaskCompletionSource.SetResult();
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

    public void Dispose() => Abort(new ConnectionClosedException());

    public async Task ShutdownAsync(ulong applicationErrorCode, CancellationToken cancel)
    {
        // Send the close frame.
        await _writeSemaphore.EnterAsync(cancel).ConfigureAwait(false);
        try
        {
            await WriteFrameAsync(
                FrameType.Close,
                streamId: null,
                new CloseBody(applicationErrorCode).Encode,
                cancel).ConfigureAwait(false);

            // Prevent further writes.
            _writeSemaphore.Complete(new ConnectionClosedException());
        }
        finally
        {
            _writeSemaphore.Release();
        }

        // Shutdown the simple network connection.
        await _simpleNetworkConnection.ShutdownAsync(cancel).ConfigureAwait(false);
    }

    internal SlicNetworkConnection(
        ISimpleNetworkConnection simpleNetworkConnection,
        bool isServer,
        IMultiplexedStreamErrorCodeConverter errorCodeConverter,
        SlicTransportOptions slicOptions)
    {
        IsServer = isServer;
        ErrorCodeConverter = errorCodeConverter;
        PauseWriterThreshold = slicOptions.PauseWriterThreshold;
        ResumeWriterThreshold = slicOptions.ResumeWriterThreshold;
        Pool = slicOptions.Pool;
        MinimumSegmentSize = slicOptions.MinimumSegmentSize;

        _localIdleTimeout = slicOptions.IdleTimeout;
        _packetMaxSize = slicOptions.PacketMaxSize;
        _bidirectionalMaxStreams = slicOptions.BidirectionalStreamMaxCount;
        _unidirectionalMaxStreams = slicOptions.UnidirectionalStreamMaxCount;
        _simpleNetworkConnection = simpleNetworkConnection;

        _simpleNetworkConnectionWriter = new SimpleNetworkConnectionWriter(
            simpleNetworkConnection,
            slicOptions.Pool,
            slicOptions.MinimumSegmentSize);

        Action? keepAliveAction = null;
        if (!IsServer)
        {
            // Only client connections send ping frames when idle to keep the connection alive.
            keepAliveAction = () => SendFrameAsync(stream: null, FrameType.Ping, null, default).AsTask();
        }

        _simpleNetworkConnectionReader = new SimpleNetworkConnectionReader(
            simpleNetworkConnection,
            slicOptions.Pool,
            slicOptions.MinimumSegmentSize,
            abortAction: Abort,
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
            if (_exception != null)
            {
                throw _exception;
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

    internal async ValueTask FillBufferWriterAsync(
        IBufferWriter<byte> bufferWriter,
        int byteCount,
        CancellationToken cancel)
    {
        try
        {
            await _simpleNetworkConnectionReader.FillBufferWriterAsync(
                bufferWriter,
                byteCount,
                cancel).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The simple network connection can only be disposed if this connection is aborted.
            lock (_mutex)
            {
                Debug.Assert(_exception != null);
                throw ExceptionUtil.Throw(_exception);
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
        if (_bidirectionalStreamSemaphore == null)
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
                    cancel).ConfigureAwait(false);
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
            EncodeParameter(ParameterKey.MaxBidirectionalStreams, (ulong)_bidirectionalMaxStreams),
            EncodeParameter(ParameterKey.MaxUnidirectionalStreams, (ulong)_unidirectionalMaxStreams),
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

<<<<<<< HEAD
    private async ValueTask<T> ReadFrameAsync<T>(
        int size,
        DecodeFunc<T> decodeFunc,
        CancellationToken cancel)
    {
        Debug.Assert(size > 0);

        try
        {
            ReadOnlySequence<byte> buffer = await _simpleNetworkConnectionReader.ReadAtLeastAsync(
                size, cancel).ConfigureAwait(false);

            if (buffer.Length > size)
            {
                buffer = buffer.Slice(0, size);
            }

            T decodedFrame = SliceEncoding.Slice2.DecodeBuffer(buffer, decodeFunc);
            _simpleNetworkConnectionReader.AdvanceTo(buffer.End);
            return decodedFrame;
        }
        catch (ObjectDisposedException)
        {
            // The simple network connection can only be disposed if this connection is aborted.
            lock (_mutex)
            {
                Debug.Assert(_exception != null);
                throw ExceptionUtil.Throw(_exception);
            }
        }
    }

    private async ValueTask<(FrameType FrameType, int FrameSize, long? StreamId)> ReadFrameHeaderAsync(
        CancellationToken cancel)
    {
        try
        {
            while (true)
            {
                // Read data from the pipe reader.
                if (!_simpleNetworkConnectionReader.TryRead(out ReadOnlySequence<byte> buffer))
                {
                    buffer = await _simpleNetworkConnectionReader.ReadAsync(cancel).ConfigureAwait(false);
                }

                if (TryDecodeHeader(
                    buffer,
                    out (FrameType FrameType, int FrameSize, long? StreamId) header,
                    out int consumed))
                {
                    _simpleNetworkConnectionReader.AdvanceTo(buffer.GetPosition(consumed));
                    return header;
                }
                else
                {
                    _simpleNetworkConnectionReader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // The simple network connection can only be disposed if this connection is aborted.
            lock (_mutex)
            {
                Debug.Assert(_exception != null);
                throw ExceptionUtil.Throw(_exception);
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

    private async Task<ulong> ReadFramesAsync(CancellationToken cancel)
=======
    private async Task ReadFramesAsync(CancellationToken cancel)
>>>>>>> origin/main
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
                    // Nothing to do, the simple network connection reader keeps track of the last activity time.
                    break;
                }
                case FrameType.Stream:
                case FrameType.StreamLast:
                {
                    Debug.Assert(streamId != null);
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

                        await _simpleNetworkConnectionReader.FillBufferWriterAsync(
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
                    Debug.Assert(streamId != null);
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
                    Debug.Assert(streamId != null);
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
                    Debug.Assert(streamId != null);
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
                    Debug.Assert(streamId != null);
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

        if (_bidirectionalStreamSemaphore == null)
        {
            throw new InvalidDataException("missing MaxBidirectionalStreams Slic connection parameter");
        }

        if (_unidirectionalStreamSemaphore == null)
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
            _simpleNetworkConnectionReader.SetIdleTimeout(peerIdleTimeoutValue);
        }
    }

    private ValueTask WriteFrameAsync(
        FrameType frameType,
        long? streamId,
        EncodeAction? encode,
        CancellationToken cancel)
    {
        var encoder = new SliceEncoder(_simpleNetworkConnectionWriter, SliceEncoding.Slice2);
        encoder.EncodeUInt8((byte)frameType);
        Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
        int startPos = encoder.EncodedByteCount;

        if (streamId != null)
        {
            encoder.EncodeVarUInt62((ulong)streamId);
        }
        encode?.Invoke(ref encoder);
        SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);

        try
        {
            return _simpleNetworkConnectionWriter.FlushAsync(cancel);
        }
        catch (ObjectDisposedException)
        {
            // The simple network connection can only be disposed if this connection is aborted.
            lock (_mutex)
            {
                Debug.Assert(_exception != null);
                throw ExceptionUtil.Throw(_exception);
            }
        }
    }

    private ValueTask WriteStreamFrameAsync(
        long streamId,
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        CancellationToken cancel)
    {
        var encoder = new SliceEncoder(_simpleNetworkConnectionWriter, SliceEncoding.Slice2);
        encoder.EncodeUInt8((byte)(endStream ? FrameType.StreamLast : FrameType.Stream));
        Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
        int startPos = encoder.EncodedByteCount;
        encoder.EncodeVarUInt62((ulong)streamId);
        SliceEncoder.EncodeVarUInt62(
            (ulong)(encoder.EncodedByteCount - startPos + source1.Length + source2.Length), sizePlaceholder);

        try
        {
            // We let the write complete if the stream write operation is canceled. The cancellation of the stream write
            // operation should not leave the simple network connection in a non recoverable state (which is typically
            // the case when a write on the network connection is canceled).
            return new(_simpleNetworkConnectionWriter.WriteAsync(
                source1,
                source2,
                CancellationToken.None).AsTask().WaitAsync(cancel));
        }
        catch (ObjectDisposedException)
        {
            // The simple network connection can only be disposed if this connection is aborted.
            lock (_mutex)
            {
                Debug.Assert(_exception != null);
                throw ExceptionUtil.Throw(_exception);
            }
        }
    }
}
