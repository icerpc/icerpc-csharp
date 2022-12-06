// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading.Channels;

namespace IceRpc.Transports.Internal;

/// <summary>The Slic connection implements an <see cref="IMultiplexedConnection" /> on top of a <see
/// cref="IDuplexConnection" />.</summary>
internal class SlicConnection : IMultiplexedConnection
{
    public ServerAddress ServerAddress => _duplexConnection.ServerAddress;

    internal bool IsServer { get; }

    internal int MinSegmentSize { get; }

    internal int PauseWriterThreshold { get; }

    internal int PeerPacketMaxSize { get; private set; }

    internal int PeerPauseWriterThreshold { get; private set; }

    internal MemoryPool<byte> Pool { get; }

    internal int ResumeWriterThreshold { get; }

    private readonly Channel<IMultiplexedStream> _acceptStreamChannel;
    private int _bidirectionalStreamCount;
    private AsyncSemaphore? _bidirectionalStreamSemaphore;
    private Task? _disposeTask;
    private readonly IDuplexConnection _duplexConnection;
    private readonly DuplexConnectionReader _duplexConnectionReader;
    private readonly DuplexConnectionWriter _duplexConnectionWriter;
    private IceRpcException? _exception;
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
    private Task? _closeTask;
    private readonly ConcurrentDictionary<ulong, SlicStream> _streams = new();
    private readonly CancellationTokenSource _tasksCts = new();
    private int _unidirectionalStreamCount;
    private AsyncSemaphore? _unidirectionalStreamSemaphore;
    private readonly AsyncSemaphore _writeSemaphore = new(1, 1);

    public async ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken)
    {
        if (_disposeTask is not null)
        {
            throw new ObjectDisposedException($"{typeof(SlicConnection)}");
        }
        if (_readFramesTask is null)
        {
            throw new InvalidOperationException(
                $"Can't call {nameof(AcceptStreamAsync)} before {nameof(ConnectAsync)}.");
        }

        try
        {
            return await _acceptStreamChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException exception)
        {
            Debug.Assert(exception.InnerException is not null);

            // The exception given to to ChannelWriter.Complete(Exception? exception) is the InnerException.
            throw ExceptionUtil.Throw(exception.InnerException);
        }
    }

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_disposeTask is not null)
        {
            throw new ObjectDisposedException($"{typeof(SlicConnection)}");
        }
        if (_readFramesTask is not null)
        {
            throw new InvalidOperationException($"Can't call {nameof(ConnectAsync)} twice.");
        }

        Debug.Assert(_exception is null);

        // Connect the duplex connection.
        TransportConnectionInformation information = await _duplexConnection.ConnectAsync(cancellationToken)
            .ConfigureAwait(false);

        // Enable the idle timeout checks after the transport connection establishment. The sending of keep alive
        // messages requires the connection to be established.
        _duplexConnectionReader.EnableAliveCheck(_localIdleTimeout);
        _duplexConnectionWriter.EnableKeepAlive(_localIdleTimeout / 2);

        TimeSpan peerIdleTimeout = TimeSpan.MaxValue;
        (FrameType FrameType, int FrameSize, ulong?)? header;

        // Initialize the Slic connection.
        if (IsServer)
        {
            // Read the Initialize frame sent by the client.
            header = await ReadFrameHeaderAsync(cancellationToken).ConfigureAwait(false);
            if (header is null || header.Value.FrameSize == 0)
            {
                throw new IceRpcException(IceRpcError.IceRpcError, "Invalid Slic initialize frame.");
            }

            if (header.Value.FrameType != FrameType.Initialize)
            {
                throw new IceRpcException(
                    IceRpcError.IceRpcError,
                    $"Unexpected Slic frame '{header.Value.FrameType}'.");
            }

            (ulong version, InitializeBody? initializeBody) = await ReadFrameAsync(
                header.Value.FrameSize,
                (ref SliceDecoder decoder) => DecodeInitialize(ref decoder, header.Value.FrameSize),
                cancellationToken).ConfigureAwait(false);

            if (version != 1)
            {
                // Unsupported version, try to negotiate another version by sending a Version frame with
                // the Slic versions supported by this server.
                await SendFrameAsync(
                    stream: null,
                    FrameType.Version,
                    new VersionBody(new ulong[] { SlicDefinitions.V1 }).Encode,
                    cancellationToken).ConfigureAwait(false);

                // Read again the Initialize frame sent by the client.
                header = await ReadFrameHeaderAsync(cancellationToken).ConfigureAwait(false);
                if (header is null || header.Value.FrameSize == 0)
                {
                    throw new IceRpcException(IceRpcError.IceRpcError, "Invalid Slic initialize frame.");
                }

                (version, initializeBody) = await ReadFrameAsync(
                    header.Value.FrameSize,
                    (ref SliceDecoder decoder) => DecodeInitialize(ref decoder, header.Value.FrameSize),
                    cancellationToken).ConfigureAwait(false);
            }

            if (initializeBody is null)
            {
                throw new NotSupportedException($"Unsupported Slic version '{version}'.");
            }

            // Check the application protocol and set the parameters.
            string protocolName = initializeBody.Value.ApplicationProtocolName;
            if (!Protocol.TryParse(protocolName, out Protocol? protocol) || protocol != Protocol.IceRpc)
            {
                throw new NotSupportedException($"Application protocol '{protocolName}' is not supported.");
            }

            SetParameters(initializeBody.Value.Parameters);

            // Write back an InitializeAck frame.
            await SendFrameAsync(
                stream: null,
                FrameType.InitializeAck,
                new InitializeAckBody(GetParameters()).Encode,
                cancellationToken).ConfigureAwait(false);
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
                cancellationToken).ConfigureAwait(false);

            // Read back either the InitializeAck or Version frame.
            header = await ReadFrameHeaderAsync(cancellationToken).ConfigureAwait(false);
            if (header is null || header.Value.FrameSize == 0)
            {
                throw new IceRpcException(IceRpcError.IceRpcError, "Invalid Slic initialize ack frame.");
            }

            switch (header.Value.FrameType)
            {
                case FrameType.InitializeAck:
                    InitializeAckBody initializeAckBody = await ReadFrameAsync(
                        header.Value.FrameSize,
                        (ref SliceDecoder decoder) => new InitializeAckBody(ref decoder),
                        cancellationToken).ConfigureAwait(false);

                    SetParameters(initializeAckBody.Parameters);
                    break;

                case FrameType.Version:
                    VersionBody versionBody = await ReadFrameAsync(
                        header.Value.FrameSize,
                        (ref SliceDecoder decoder) => new VersionBody(ref decoder),
                        cancellationToken).ConfigureAwait(false);

                    // We currently only support V1
                    throw new NotSupportedException(
                        $"Unsupported Slic versions '{string.Join(", ", versionBody.Versions)}'.");

                default:
                    throw new IceRpcException(
                        IceRpcError.IceRpcError,
                        $"Unexpected Slic frame '{header.Value.FrameType}'.");
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

                    if (IsServer)
                    {
                        // The server-side of the connection is only shutdown once the client-side is shutdown. When
                        // using TCP, this ensures that the server TCP connection won't end-up in the TIME_WAIT state.
                        await _duplexConnection.ShutdownAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Nothing to do, DisposeAsync has been called and it takes care of the cleanup.
                }
                catch (IceRpcException exception)
                {
                    // Unexpected transport exception.
                    await CloseAsyncCore(exception).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // Unexpected exception.
                    await CloseAsyncCore(new IceRpcException(IceRpcError.IceRpcError, exception)).ConfigureAwait(false);
                }
                finally
                {
                    lock (_mutex)
                    {
                        Debug.Assert(_exception is not null);
                    }

                    _duplexConnection.Dispose();

                    // Time for AcceptStreamAsync to return.
                    _acceptStreamChannel.Writer.TryComplete(_exception);
                }
            },
            CancellationToken.None);

        return information;

        static (uint, InitializeBody?) DecodeInitialize(ref SliceDecoder decoder, int frameSize)
        {
            uint version = decoder.DecodeVarUInt32();
            if (version == SlicDefinitions.V1)
            {
                return (version, new InitializeBody(ref decoder));
            }
            else
            {
                decoder.Skip(frameSize - (int)decoder.Consumed);
                return (version, null);
            }
        }
    }

    public async Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(SlicConnection)}");
            }
            if (_readFramesTask is null)
            {
                throw new InvalidOperationException($"Can't call {nameof(CloseAsync)} before {nameof(ConnectAsync)}.");
            }
            if (_exception is not null)
            {
                if (_exception.IceRpcError == IceRpcError.ConnectionClosedByPeer ||
                    _exception.IceRpcError == IceRpcError.ConnectionAborted)
                {
                    // The peer already closed the connection, there's nothing to close so just return.
                    return;
                }
                else
                {
                    throw ExceptionUtil.Throw(_exception);
                }
            }

            // The close task might already be set if the peer closed the connection.
            _closeTask ??= PerformCloseAsync();
        }

        await _closeTask.ConfigureAwait(false);

        // Wait for the termination of the read frames task. The task is null if ConnectAsync wasn't called.
        await _readFramesTask.ConfigureAwait(false);

        async Task PerformCloseAsync()
        {
            var exception = new IceRpcException(IceRpcError.ConnectionAborted);

            // Send close frame if the connection is connected or if it's a server connection (to reject the connection
            // establishment from the client).
            if (await CloseAsyncCore(exception).ConfigureAwait(false))
            {
                // Send the close frame.
                await WriteFrameAsync(
                    FrameType.Close,
                    streamId: null,
                    new CloseBody((ulong)closeError).Encode,
                    cancellationToken).ConfigureAwait(false);
            }

            if (!IsServer)
            {
                // The sending of the client-side Close frame is followed by the shutdown of the duplex connection. For
                // TCP, it's important to always shutdown the connection on the client-side first to avoid TIME_WAIT
                // states on the server-side.
                await _duplexConnection.ShutdownAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public ValueTask<IMultiplexedStream> CreateStreamAsync(bool bidirectional, CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(SlicConnection)}");
            }
            if (_readFramesTask is null)
            {
                throw new InvalidOperationException(
                    $"Can't call {nameof(CreateStreamAsync)} before {nameof(ConnectAsync)}.");
            }
            if (_exception is not null)
            {
                throw ExceptionUtil.Throw(_exception);
            }

            // TODO: Cache SlicStream and implement stream max count flow control here like Quic?
            return new(new SlicStream(this, bidirectional, remote: false));
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            _disposeTask ??= PerformDisposeAsync();
        }
        return new(_disposeTask);

        async Task PerformDisposeAsync()
        {
            await CloseAsyncCore(new IceRpcException(IceRpcError.OperationAborted)).ConfigureAwait(false);

            // Cancel tasks which are using the transport connection before disposing the transport connection.
            _tasksCts.Cancel();

            if (_readFramesTask is not null)
            {
                await _readFramesTask.ConfigureAwait(false);
            }

            // Dispose the transport connection and the reader/writer.
            _duplexConnection.Dispose();
            _duplexConnectionReader.Dispose();
            _duplexConnectionWriter.Dispose();

            _tasksCts.Dispose();
        }
    }

    internal SlicConnection(
        IDuplexConnection duplexConnection,
        MultiplexedConnectionOptions options,
        SlicTransportOptions slicOptions,
        bool isServer)
    {
        IsServer = isServer;

        Pool = options.Pool;
        MinSegmentSize = options.MinSegmentSize;
        _maxBidirectionalStreams = options.MaxBidirectionalStreams;
        _maxUnidirectionalStreams = options.MaxUnidirectionalStreams;

        PauseWriterThreshold = slicOptions.PauseWriterThreshold;
        ResumeWriterThreshold = slicOptions.ResumeWriterThreshold;
        _localIdleTimeout = slicOptions.IdleTimeout;
        _packetMaxSize = slicOptions.PacketMaxSize;

        _duplexConnection = duplexConnection;

        _acceptStreamChannel = Channel.CreateUnbounded<IMultiplexedStream>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        Action? keepAliveAction = null;
        if (!IsServer)
        {
            // Only client connections send ping frames when idle to keep the connection alive.
            keepAliveAction = () => SendFrameAsync(stream: null, FrameType.Ping, null, default).AsTask();
        }

        _duplexConnectionWriter = new DuplexConnectionWriter(
            duplexConnection,
            options.Pool,
            options.MinSegmentSize,
            keepAliveAction);

        _duplexConnectionReader = new DuplexConnectionReader(
            duplexConnection,
            options.Pool,
            options.MinSegmentSize,
            connectionLostAction: exception => _acceptStreamChannel.Writer.TryComplete(exception));

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
        CancellationToken cancellationToken) =>
        _duplexConnectionReader.FillBufferWriterAsync(bufferWriter, byteCount, cancellationToken);

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
        CancellationToken cancellationToken)
    {
        await _writeSemaphore.EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteFrameAsync(
                frameType,
                stream?.Id,
                encode,
                cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        Debug.Assert(!source1.IsEmpty || endStream);
        if (_bidirectionalStreamSemaphore is null)
        {
            throw new InvalidOperationException($"Cannot send a stream before calling {nameof(ConnectAsync)}.");
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
                await streamCountSemaphore.EnterAsync(cancellationToken).ConfigureAwait(false);
            }

            // Next, ensure send credit is available. If not, this will block until the receiver allows sending
            // additional data.
            int sendCredit = await stream.AcquireSendCreditAsync(cancellationToken).ConfigureAwait(false);
            Debug.Assert(sendCredit > 0);

            // Finally, acquire the write semaphore to ensure only one stream writes to the connection.
            await _writeSemaphore.EnterAsync(cancellationToken).ConfigureAwait(false);
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
                    _tasksCts.Token).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_tasksCts.IsCancellationRequested)
            {
                throw new IceRpcException(IceRpcError.OperationAborted);
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }
        while (!source1.IsEmpty || !source2.IsEmpty); // Loop until there's no data left to send.

        return new FlushResult(isCanceled: false, isCompleted: false);
    }

    private async ValueTask<bool> CloseAsyncCore(IceRpcException exception)
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

        await _writeSemaphore.CompleteAndWaitAsync(exception).ConfigureAwait(false);

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
        CancellationToken cancellationToken)
    {
        Debug.Assert(size > 0);

        ReadOnlySequence<byte> buffer = await _duplexConnectionReader.ReadAtLeastAsync(
            size, cancellationToken).ConfigureAwait(false);

        if (buffer.Length > size)
        {
            buffer = buffer.Slice(0, size);
        }

        T decodedFrame = SliceEncoding.Slice2.DecodeBuffer(buffer, decodeFunc);
        _duplexConnectionReader.AdvanceTo(buffer.End);
        return decodedFrame;
    }

    private async ValueTask<(FrameType FrameType, int FrameSize, ulong? StreamId)?> ReadFrameHeaderAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            // Read data from the pipe reader.
            if (!_duplexConnectionReader.TryRead(out ReadOnlySequence<byte> buffer))
            {
                buffer = await _duplexConnectionReader.ReadAsync(cancellationToken).ConfigureAwait(false);
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
            if (!decoder.TryDecodeUInt8(out byte frameType) ||
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

    private async Task ReadFramesAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            (FrameType, int, ulong?)? header = await ReadFrameHeaderAsync(cancellationToken).ConfigureAwait(false);
            if (header is null)
            {
                lock (_mutex)
                {
                    if (_exception is null)
                    {
                        throw new IceRpcException(IceRpcError.IceRpcError);
                    }
                }
                return;
            }

            (FrameType type, int dataSize, ulong? streamId) = header.Value;

            switch (type)
            {
                case FrameType.Close:
                {
                    CloseBody closeBody = await ReadFrameAsync(
                        dataSize,
                        (ref SliceDecoder decoder) => new CloseBody(ref decoder),
                        cancellationToken).ConfigureAwait(false);

                    lock (_mutex)
                    {
                        // If close is not already in progress initiate the closure.
                        _closeTask ??= PerformCloseAsync(closeBody.ApplicationErrorCode);
                    }
                    await _closeTask.ConfigureAwait(false);
                    break;
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
                        throw new IceRpcException(
                            IceRpcError.IceRpcError,
                            "Received Slic stream frame on local unidirectional stream.");
                    }
                    else if (dataSize == 0 && !endStream)
                    {
                        throw new IceRpcException(
                            IceRpcError.IceRpcError,
                            "Invalid Slic stream frame, received 0 bytes without end of stream.");
                    }

                    int readSize = 0;
                    if (_streams.TryGetValue(streamId.Value, out SlicStream? stream))
                    {
                        // Let the stream receive the data.
                        readSize = await stream.ReceivedStreamFrameAsync(
                            dataSize,
                            endStream,
                            cancellationToken).ConfigureAwait(false);
                    }
                    else if (isRemote && !IsKnownRemoteStream(streamId.Value, isBidirectional))
                    {
                        // Create a new stream if the remote stream is unknown.

                        if (dataSize == 0)
                        {
                            throw new IceRpcException(
                                IceRpcError.IceRpcError,
                                "Received empty Slic stream frame on new stream.");
                        }

                        if (isBidirectional)
                        {
                            if (_bidirectionalStreamCount == _maxBidirectionalStreams)
                            {
                                throw new IceRpcException(
                                    IceRpcError.IceRpcError,
                                    $"Maximum bidirectional stream count {_maxBidirectionalStreams} reached.");
                            }
                            Interlocked.Increment(ref _bidirectionalStreamCount);
                        }
                        else
                        {
                            if (_unidirectionalStreamCount == _maxUnidirectionalStreams)
                            {
                                throw new IceRpcException(
                                    IceRpcError.IceRpcError,
                                    $"Maximum unidirectional stream count {_maxUnidirectionalStreams} reached.");
                            }
                            Interlocked.Increment(ref _unidirectionalStreamCount);
                        }

                        // Accept the new remote stream.
                        // TODO: Cache SliceMultiplexedStream
                        stream = new SlicStream(this, isBidirectional, remote: true);

                        try
                        {
                            AddStream(streamId.Value, stream);

                            // Let the stream receive the data.
                            readSize = await stream.ReceivedStreamFrameAsync(
                                dataSize,
                                endStream,
                                cancellationToken).ConfigureAwait(false);

                            // Queue the new stream only if it read the full size (otherwise, it has been shutdown).
                            if (readSize == dataSize)
                            {
                                try
                                {
                                    await _acceptStreamChannel.Writer.WriteAsync(
                                        stream,
                                        cancellationToken).ConfigureAwait(false);
                                }
                                catch (ChannelClosedException exception)
                                {
                                    Debug.Assert(exception.InnerException is not null);

                                    // The exception given to to ChannelWriter.Complete(Exception? exception) is the
                                    // InnerException.
                                    throw ExceptionUtil.Throw(exception.InnerException);
                                }
                            }
                        }
                        catch
                        {
                            stream.Input.Complete();
                            if (isBidirectional)
                            {
                                stream.Output.Complete();
                            }
                            Debug.Assert(stream.IsShutdown);
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
                                cancellationToken).ConfigureAwait(false);

                        pipe.Writer.Complete();
                        pipe.Reader.Complete();
                    }

                    break;
                }
                case FrameType.StreamConsumed:
                {
                    Debug.Assert(streamId is not null);
                    if (dataSize == 0)
                    {
                        throw new IceRpcException(IceRpcError.IceRpcError, "Slic stream consumed frame too small.");
                    }
                    else if (dataSize > 8)
                    {
                        throw new IceRpcException(IceRpcError.IceRpcError, "Slic stream consumed frame too large.");
                    }

                    StreamConsumedBody consumed = await ReadFrameAsync(
                        dataSize,
                        (ref SliceDecoder decoder) => new StreamConsumedBody(ref decoder),
                        cancellationToken).ConfigureAwait(false);
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
                        throw new IceRpcException(IceRpcError.IceRpcError, "Slic stream reset frame too small.");
                    }
                    else if (dataSize > 8)
                    {
                        throw new IceRpcException(IceRpcError.IceRpcError, "Slic stream reset frame too large.");
                    }

                    StreamResetBody streamReset = await ReadFrameAsync(
                        dataSize,
                        (ref SliceDecoder decoder) => new StreamResetBody(ref decoder),
                        cancellationToken).ConfigureAwait(false);
                    if (_streams.TryGetValue(streamId.Value, out SlicStream? stream))
                    {
                        stream.ReceivedResetFrame();
                    }
                    break;
                }
                case FrameType.StreamStopSending:
                {
                    Debug.Assert(streamId is not null);
                    if (dataSize == 0)
                    {
                        throw new IceRpcException(IceRpcError.IceRpcError, "Slic stream stop sending frame too small.");
                    }
                    else if (dataSize > 8)
                    {
                        throw new IceRpcException(IceRpcError.IceRpcError, "Slic stream stop sending frame too large.");
                    }

                    StreamStopSendingBody streamStopSending = await ReadFrameAsync(
                        dataSize,
                        (ref SliceDecoder decoder) => new StreamStopSendingBody(ref decoder),
                        cancellationToken).ConfigureAwait(false);
                    if (_streams.TryGetValue(streamId.Value, out SlicStream? stream))
                    {
                        stream.ReceivedStopSendingFrame();
                    }
                    break;
                }
                case FrameType.UnidirectionalStreamReleased:
                {
                    Debug.Assert(streamId is not null);
                    if (dataSize > 0)
                    {
                        throw new IceRpcException(
                            IceRpcError.IceRpcError,
                            "Slic unidirectional stream released frame too large.");
                    }

                    if (_streams.TryGetValue(streamId.Value, out SlicStream? stream))
                    {
                        stream.ReceivedUnidirectionalStreamReleasedFrame();
                    }

                    // Release the unidirectional stream semaphore for the unidirectional stream.
                    _unidirectionalStreamSemaphore!.Release();
                    break;
                }
                default:
                {
                    throw new IceRpcException(IceRpcError.IceRpcError, $"Unexpected Slic frame '{type}'.");
                }
            }
        }

        async Task PerformCloseAsync(ulong errorCode)
        {
            IceRpcException exception = errorCode switch
            {
                (ulong)MultiplexedConnectionCloseError.NoError =>
                    new IceRpcException(IceRpcError.ConnectionClosedByPeer),
                (ulong)MultiplexedConnectionCloseError.ServerBusy =>
                    new IceRpcException(IceRpcError.ServerBusy),
                _ => new IceRpcException(
                    IceRpcError.ConnectionAborted,
                    $"The connection was closed by the peer with unknown application error code {errorCode}.")
            };

            if (await CloseAsyncCore(exception).ConfigureAwait(false))
            {
                if (IsServer)
                {
                    // The sending of the client-side Close frame is always followed by the shutdown of the duplex
                    // connection. We wait for the shutdown of the duplex connection instead of returning here. We want
                    // to make sure the duplex connection is always shutdown on the client-side before shutting it down
                    // on the server-side. It's important when using TCP to avoid TIME_WAIT states on the server-side.
                }
                else
                {
                    await _duplexConnection.ShutdownAsync(cancellationToken).ConfigureAwait(false);
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
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "Missing MaxBidirectionalStreams Slic connection parameter.");
        }

        if (_unidirectionalStreamSemaphore is null)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "Missing MaxUnidirectionalStreams Slic connection parameter.");
        }

        if (PeerPacketMaxSize < 1024)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                $"Invalid PacketMaxSize={PeerPacketMaxSize} Slic connection parameter.");
        }

        // Use the smallest idle timeout.
        if (peerIdleTimeout is TimeSpan peerIdleTimeoutValue && peerIdleTimeoutValue < _localIdleTimeout)
        {
            _duplexConnectionReader.EnableAliveCheck(peerIdleTimeoutValue);
            _duplexConnectionWriter.EnableKeepAlive(peerIdleTimeoutValue / 2);
        }
    }

    private ValueTask WriteFrameAsync(
        FrameType frameType,
        ulong? streamId,
        EncodeAction? encode,
        CancellationToken cancellationToken)
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

        return _duplexConnectionWriter.FlushAsync(cancellationToken);
    }

    private ValueTask WriteStreamFrameAsync(
        ulong streamId,
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        CancellationToken cancellationToken)
    {
        var encoder = new SliceEncoder(_duplexConnectionWriter, SliceEncoding.Slice2);
        encoder.EncodeFrameType(endStream ? FrameType.StreamLast : FrameType.Stream);
        Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
        int startPos = encoder.EncodedByteCount;
        encoder.EncodeVarUInt62(streamId);
        SliceEncoder.EncodeVarUInt62(
            (ulong)(encoder.EncodedByteCount - startPos + source1.Length + source2.Length), sizePlaceholder);

        return _duplexConnectionWriter.WriteAsync(source1, source2, cancellationToken);
    }
}
