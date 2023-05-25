// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Security.Authentication;
using System.Threading.Channels;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>The Slic connection implements an <see cref="IMultiplexedConnection" /> on top of a <see
/// cref="IDuplexConnection" />.</summary>
internal class SlicConnection : IMultiplexedConnection
{
    internal bool IsServer { get; }

    internal int MinSegmentSize { get; }

    internal int PauseWriterThreshold { get; }

    internal int PeerPacketMaxSize { get; private set; }

    internal int PeerPauseWriterThreshold { get; private set; }

    internal MemoryPool<byte> Pool { get; }

    internal int ResumeWriterThreshold { get; }

    private readonly Channel<IMultiplexedStream> _acceptStreamChannel;
    private int _bidirectionalStreamCount;
    private SemaphoreSlim? _bidirectionalStreamSemaphore;
    private readonly CancellationTokenSource _closedCts = new();
    private readonly CancellationToken _closedCancellationToken;
    private string? _closedMessage;
    private Task? _closeTask;
    private Task<TransportConnectionInformation>? _connectTask;
    private readonly CancellationTokenSource _disposedCts = new();
    private Task? _disposeTask;
    private readonly IDuplexConnection _duplexConnection;
    private readonly DuplexConnectionReader _duplexConnectionReader;
    private readonly SlicDuplexConnectionWriter _duplexConnectionWriter;
    private readonly Action<TimeSpan, Action?> _enableIdleTimeoutAndKeepAlive;
    private bool _isClosed;
    private ulong? _lastRemoteBidirectionalStreamId;
    private ulong? _lastRemoteUnidirectionalStreamId;
    private readonly TimeSpan _localIdleTimeout;
    private readonly int _maxBidirectionalStreams;
    private readonly int _maxUnidirectionalStreams;
    // _mutex ensure the assignment of _lastRemoteXxx members and the addition of the stream to _streams is
    // an atomic operation.
    private readonly object _mutex = new();
    private ulong _nextBidirectionalId;
    private ulong _nextUnidirectionalId;
    private readonly int _packetMaxSize;
    private IceRpcError? _peerCloseError;
    private TimeSpan _peerIdleTimeout = Timeout.InfiniteTimeSpan;
    private Task _pingTask = Task.CompletedTask;
    private Task _pongTask = Task.CompletedTask;
    private Task? _readFramesTask;

    private readonly ConcurrentDictionary<ulong, SlicStream> _streams = new();
    private int _streamSemaphoreWaitCount;
    private readonly TaskCompletionSource _streamSemaphoreWaitClosed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _unidirectionalStreamCount;
    private SemaphoreSlim? _unidirectionalStreamSemaphore;
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

    public async ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);

            if (_connectTask is null || !_connectTask.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Cannot accept stream before connecting the Slic connection.");
            }
            if (_isClosed)
            {
                throw new IceRpcException(_peerCloseError ?? IceRpcError.ConnectionAborted, _closedMessage);
            }
        }

        try
        {
            return await _acceptStreamChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException exception)
        {
            Debug.Assert(exception.InnerException is not null);

            // The exception given to ChannelWriter.Complete(Exception? exception) is the InnerException.
            throw ExceptionUtil.Throw(exception.InnerException);
        }
    }

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);

            if (_connectTask is not null)
            {
                throw new InvalidOperationException("Cannot connect twice a Slic connection.");
            }
            if (_isClosed)
            {
                throw new InvalidOperationException("Cannot connect a closed Slic connection.");
            }
            _connectTask = PerformConnectAsync();
        }
        return _connectTask;

        async Task<TransportConnectionInformation> PerformConnectAsync()
        {
            await Task.Yield(); // Exit mutex lock

            // Connect the duplex connection.
            TransportConnectionInformation transportConnectionInformation;
            TimeSpan peerIdleTimeout = TimeSpan.MaxValue;

            try
            {
                transportConnectionInformation = await _duplexConnection.ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);

                // Initialize the Slic connection.
                if (IsServer)
                {
                    // Read the Initialize frame.
                    (ulong version, InitializeBody? initializeBody) = await ReadFrameAsync(
                        DecodeInitialize,
                        cancellationToken).ConfigureAwait(false);

                    if (version != 1)
                    {
                        // Unsupported version, try to negotiate another version by sending a Version frame with the
                        // Slic versions supported by this server.
                        await SendFrameAsync(
                            FrameType.Version,
                            new VersionBody(new ulong[] { SlicDefinitions.V1 }).Encode,
                            cancellationToken).ConfigureAwait(false);

                        (version, initializeBody) = await ReadFrameAsync(
                            DecodeInitialize,
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (initializeBody is null)
                    {
                        throw new IceRpcException(
                            IceRpcError.ConnectionAborted,
                            $"The connection was aborted because the peer's Slic version '{version}' is not supported.");
                    }

                    // Check the application protocol and set the parameters.
                    string protocolName = initializeBody.Value.ApplicationProtocolName;
                    if (!Protocol.TryParse(protocolName, out Protocol? protocol) || protocol != Protocol.IceRpc)
                    {
                        throw new IceRpcException(
                            IceRpcError.ConnectionAborted,
                            $"The connection was aborted because the peer's application protocol '{protocolName}' is not supported.");
                    }

                    DecodeParameters(initializeBody.Value.Parameters);

                    // Write back an InitializeAck frame.
                    await SendFrameAsync(
                        FrameType.InitializeAck,
                        new InitializeAckBody(EncodeParameters()).Encode,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Write the Initialize frame.
                    await SendFrameAsync(
                        FrameType.Initialize,
                        (ref SliceEncoder encoder) =>
                        {
                            encoder.EncodeVarUInt62(SlicDefinitions.V1);
                            new InitializeBody(Protocol.IceRpc.Name, EncodeParameters()).Encode(ref encoder);
                        },
                        cancellationToken).ConfigureAwait(false);

                    // Read the Initialize frame.
                    InitializeAckBody initializeAckBody = await ReadFrameAsync(
                        DecodeInitializeAckOrVersion,
                        cancellationToken).ConfigureAwait(false);

                    DecodeParameters(initializeAckBody.Parameters);
                }
            }
            catch (InvalidDataException exception)
            {
                throw new IceRpcException(
                    IceRpcError.IceRpcError,
                    "The connection was aborted by a Slic protocol error.",
                    exception);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AuthenticationException)
            {
                throw;
            }
            catch (IceRpcException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.Fail($"ConnectAsync failed with an unexpected exception: {exception}");
                throw;
            }

            // Enable the idle timeout checks after the connection establishment. The Ping frames sent by the keep alive
            // check are not expected until the Slic connection initialization completes. The idle timeout check uses
            // the smallest idle timeout.
            TimeSpan idleTimeout = _peerIdleTimeout == Timeout.InfiniteTimeSpan ? _localIdleTimeout :
                (_peerIdleTimeout < _localIdleTimeout ? _peerIdleTimeout : _localIdleTimeout);

            if (idleTimeout != Timeout.InfiniteTimeSpan)
            {
                // Only client connections send ping frames when idle to keep the server connection alive. The server
                // sends back a Pong frame in turn to keep alive the client connection.
                _enableIdleTimeoutAndKeepAlive(idleTimeout, IsServer ? null : KeepAlive);
            }

            _readFramesTask = ReadFramesAsync(_disposedCts.Token);

            return transportConnectionInformation;
        }

        static (uint, InitializeBody?) DecodeInitialize(FrameType frameType, ReadOnlySequence<byte> buffer)
        {
            if (frameType != FrameType.Initialize)
            {
                throw new InvalidDataException($"Received unexpected Slic frame: '{frameType}'.");
            }

            return SliceEncoding.Slice2.DecodeBuffer<(uint, InitializeBody?)>(
                buffer,
                (ref SliceDecoder decoder) =>
                {
                    uint version = decoder.DecodeVarUInt32();
                    if (version == SlicDefinitions.V1)
                    {
                        return (version, new InitializeBody(ref decoder));
                    }
                    else
                    {
                        decoder.Skip((int)(buffer.Length - decoder.Consumed));
                        return (version, null);
                    }
                });
        }

        static InitializeAckBody DecodeInitializeAckOrVersion(FrameType frameType, ReadOnlySequence<byte> buffer)
        {
            switch (frameType)
            {
                case FrameType.InitializeAck:
                    return SliceEncoding.Slice2.DecodeBuffer(
                        buffer,
                        (ref SliceDecoder decoder) => new InitializeAckBody(ref decoder));

                case FrameType.Version:
                    // We currently only support V1
                    VersionBody versionBody = SliceEncoding.Slice2.DecodeBuffer(
                        buffer,
                        (ref SliceDecoder decoder) => new VersionBody(ref decoder));
                    throw new IceRpcException(
                        IceRpcError.ConnectionAborted,
                        $"The connection was aborted because the peer's Slic version(s) '{string.Join(", ", versionBody.Versions)}' are not supported.");

                default:
                    throw new InvalidDataException($"Received unexpected Slic frame: '{frameType}'.");
            }
        }

        void KeepAlive()
        {
            lock (_mutex)
            {
                // Send a new ping frame if the previous frame was sent and the connection is not closed
                // or being close. The check for _isClosed ensures _pingTask is not reassigned once the
                // connection is closed.
                if (_pingTask.IsCompleted && !_isClosed)
                {
                    _pingTask = SendPingFrameAsync();
                }
            }

            async Task SendPingFrameAsync()
            {
                await Task.Yield(); // Exit mutex lock
                try
                {
                    await SendFrameAsync(FrameType.Ping, encode: null, CancellationToken.None).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected if the connection was closed.
                }
                catch (IceRpcException)
                {
                    // Expected if the connection failed.
                }
                catch (Exception exception)
                {
                    Debug.Fail($"ping task failed with an unexpected exception: {exception}");
                    throw;
                }
            }
        }

        async ValueTask<T> ReadFrameAsync<T>(
            Func<FrameType, ReadOnlySequence<byte>, T> decodeFunc,
            CancellationToken cancellationToken)
        {
            (FrameType FrameType, int FrameSize, ulong?)? header =
                await ReadFrameHeaderAsync(cancellationToken).ConfigureAwait(false);

            if (header is null || header.Value.FrameSize == 0)
            {
                throw new InvalidDataException("Received invalid Slic frame.");
            }

            ReadOnlySequence<byte> buffer = await _duplexConnectionReader.ReadAtLeastAsync(
                header.Value.FrameSize,
                cancellationToken).ConfigureAwait(false);

            if (buffer.Length > header.Value.FrameSize)
            {
                buffer = buffer.Slice(0, header.Value.FrameSize);
            }

            T decodedFrame = decodeFunc(header.Value.FrameType, buffer);
            _duplexConnectionReader.AdvanceTo(buffer.End);
            return decodedFrame;
        }
    }

    public async Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);

            if (_connectTask is null || !_connectTask.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Cannot close a Slic connection before connecting it.");
            }

            // The close task might already be set if the peer closed the connection.
            _closeTask ??= PerformCloseAsync();
        }

        // Wait for the sending of the close frame.
        await _closeTask.ConfigureAwait(false);

        // Now, wait for the peer to send the close frame that will terminate the read frames task.
        Debug.Assert(_readFramesTask is not null);
        await _readFramesTask.WaitAsync(cancellationToken).ConfigureAwait(false);

        async Task PerformCloseAsync()
        {
            await Task.Yield(); // Exit mutex lock

            Close(new IceRpcException(IceRpcError.OperationAborted), "The connection was closed.");

            // The semaphore can't be disposed until the close task completes.
            using SemaphoreLock _ = await _writeSemaphore.AcquireAsync(cancellationToken).ConfigureAwait(false);
            WriteFrame(FrameType.Close, streamId: null, new CloseBody((ulong)closeError).Encode);
            await _duplexConnectionWriter.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (!IsServer)
            {
                // The sending of the client-side Close frame is followed by the shutdown of the duplex connection. For
                // TCP, it's important to always shutdown the connection on the client-side first to avoid TIME_WAIT
                // states on the server-side.
                _duplexConnectionWriter.Shutdown();
            }
        }
    }

    public async ValueTask<IMultiplexedStream> CreateStreamAsync(
        bool bidirectional,
        CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);

            if (_connectTask is null || !_connectTask.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Cannot create stream before connecting the Slic connection.");
            }
            if (_isClosed)
            {
                throw new IceRpcException(_peerCloseError ?? IceRpcError.ConnectionAborted, _closedMessage);
            }

            ++_streamSemaphoreWaitCount;
        }

        try
        {
            using var createStreamCts = CancellationTokenSource.CreateLinkedTokenSource(
                _closedCancellationToken,
                cancellationToken);

            SemaphoreSlim? streamCountSemaphore = bidirectional ?
                _bidirectionalStreamSemaphore :
                _unidirectionalStreamSemaphore;

            if (streamCountSemaphore is null)
            {
                // The stream semaphore is null if the peer's max streams configuration is 0. In this case, we let
                // CreateStreamAsync hang indefinitely until the connection is closed.
                await Task.Delay(-1, createStreamCts.Token).ConfigureAwait(false);
            }
            else
            {
                await streamCountSemaphore.WaitAsync(createStreamCts.Token).ConfigureAwait(false);
            }

            // TODO: Cache SlicStream
            return new SlicStream(this, bidirectional, remote: false);
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Debug.Assert(_isClosed);
            throw new IceRpcException(_peerCloseError ?? IceRpcError.OperationAborted, _closedMessage);
        }
        finally
        {
            lock (_mutex)
            {
                --_streamSemaphoreWaitCount;
                if (_isClosed && _streamSemaphoreWaitCount == 0)
                {
                    _streamSemaphoreWaitClosed.SetResult();
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Close(new IceRpcException(IceRpcError.OperationAborted), "The connection was disposed.");

        lock (_mutex)
        {
            _disposeTask ??= PerformDisposeAsync();
        }
        return new(_disposeTask);

        async Task PerformDisposeAsync()
        {
            // Make sure we execute the code below without holding the mutex lock.
            await Task.Yield();

            _disposedCts.Cancel();

            try
            {
                await Task.WhenAll(
                    _connectTask ?? Task.CompletedTask,
                    _readFramesTask ?? Task.CompletedTask,
                    _writeSemaphore.WaitAsync(CancellationToken.None),
                    _streamSemaphoreWaitClosed.Task,
                    _pingTask,
                    _pongTask,
                    _closeTask ?? Task.CompletedTask).ConfigureAwait(false);
            }
            catch
            {
                // Expected if any of these tasks failed or was canceled. Each task takes care of handling unexpected
                // exceptions so there's no need to handle them here.
            }

            // Clean-up the streams that might still be queued on the channel.
            while (_acceptStreamChannel.Reader.TryRead(out IMultiplexedStream? stream))
            {
                if (stream.IsBidirectional)
                {
                    stream.Output.Complete();
                    stream.Input.Complete();
                }
                else if (stream.IsRemote)
                {
                    stream.Input.Complete();
                }
                else
                {
                    stream.Output.Complete();
                }
            }

            try
            {
                await _acceptStreamChannel.Reader.Completion.ConfigureAwait(false);
            }
            catch
            {
            }

            _duplexConnection.Dispose();
            _duplexConnectionReader.Dispose();
            await _duplexConnectionWriter.DisposeAsync().ConfigureAwait(false);

            _disposedCts.Dispose();
            _writeSemaphore.Dispose();
            _bidirectionalStreamSemaphore?.Dispose();
            _unidirectionalStreamSemaphore?.Dispose();
            _closedCts.Dispose();
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

        _acceptStreamChannel = Channel.CreateUnbounded<IMultiplexedStream>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        _closedCancellationToken = _closedCts.Token;

        var duplexConnectionDecorator = new IdleTimeoutDuplexConnectionDecorator(duplexConnection);
        _enableIdleTimeoutAndKeepAlive = duplexConnectionDecorator.Enable;

        _duplexConnection = duplexConnectionDecorator;
        _duplexConnectionReader = new DuplexConnectionReader(_duplexConnection, options.Pool, options.MinSegmentSize);
        _duplexConnectionWriter = new SlicDuplexConnectionWriter(
            _duplexConnection,
            options.Pool,
            options.MinSegmentSize);

        // Initially set the peer packet max size to the local max size to ensure we can receive the first initialize
        // frame.
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

        _streams.Remove(stream.Id, out SlicStream? _);

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
        else if (!_isClosed)
        {
            if (stream.IsBidirectional)
            {
                _bidirectionalStreamSemaphore!.Release();
            }
            else
            {
                _unidirectionalStreamSemaphore!.Release();
            }
        }
    }

    internal async ValueTask SendFrameAsync(
        FrameType frameType,
        EncodeAction? encode,
        CancellationToken cancellationToken)
    {
        using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(
            _closedCancellationToken,
            cancellationToken);

        try
        {
            using SemaphoreLock _ = await AcquireWriteLockAsync(writeCts.Token).ConfigureAwait(false);
            WriteFrame(frameType, streamId: null, encode);
            await _duplexConnectionWriter.FlushAsync(writeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Debug.Assert(_isClosed);
            throw new IceRpcException(_peerCloseError ?? IceRpcError.ConnectionAborted, _closedMessage);
        }
    }

    internal async ValueTask SendStreamFrameAsync(
        SlicStream stream,
        FrameType frameType,
        EncodeAction? encode,
        bool sendReadsCompletedFrame)
    {
        Debug.Assert(frameType >= FrameType.StreamReset);
        try
        {
            using SemaphoreLock _ = await AcquireWriteLockAsync(_closedCancellationToken).ConfigureAwait(false);
            if (!stream.IsStarted)
            {
                StartStream(stream);
            }

            WriteFrame(frameType, stream.Id, encode);

            if (sendReadsCompletedFrame)
            {
                WriteFrame(FrameType.StreamReadsCompleted, stream.Id, encode: null);
            }

            await _duplexConnectionWriter.FlushAsync(_closedCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Debug.Assert(_isClosed);
            throw new IceRpcException(_peerCloseError ?? IceRpcError.ConnectionAborted, _closedMessage);
        }
    }

    internal async ValueTask<FlushResult> SendStreamFrameAsync(
        SlicStream stream,
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        bool sendReadsCompletedFrame,
        CancellationToken cancellationToken)
    {
        Debug.Assert(!source1.IsEmpty || endStream);
        if (_connectTask is null)
        {
            throw new InvalidOperationException("Cannot send a stream frame before calling ConnectAsync.");
        }
        using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(
            _closedCancellationToken,
            cancellationToken);

        try
        {
            do
            {
                // Next, ensure send credit is available. If not, this will block until the receiver allows sending
                // additional data.
                int sendCredit = 0;
                if (!source1.IsEmpty || !source2.IsEmpty)
                {
                    sendCredit = await stream.AcquireSendCreditAsync(writeCts.Token).ConfigureAwait(false);
                    Debug.Assert(sendCredit > 0);
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

                // Finally, acquire the write semaphore to ensure only one stream writes to the connection.
                using SemaphoreLock semaphoreLock = await AcquireWriteLockAsync(writeCts.Token).ConfigureAwait(false);
                if (!stream.IsStarted)
                {
                    StartStream(stream);
                }

                // Notify the stream that we're consuming sendSize credit. It's important to call this before sending
                // the stream frame to avoid race conditions where the StreamConsumed frame could be received before the
                // send credit was updated.
                if (sendCredit > 0)
                {
                    stream.ConsumedSendCredit((int)(sendSource1.Length + sendSource2.Length));
                }

                EncodeStreamFrameHeader(stream.Id, sendSource1.Length + sendSource2.Length, lastStreamFrame);

                if (lastStreamFrame)
                {
                    // Notify the stream that the last stream frame is considered sent at this point. This will complete
                    // writes on the stream and allow the stream to be released if reads are also completed.
                    stream.SentLastStreamFrame();
                }

                // Write and flush the stream frame.
                if (!sendSource1.IsEmpty)
                {
                    _duplexConnectionWriter.Write(sendSource1);
                }
                if (!sendSource2.IsEmpty)
                {
                    _duplexConnectionWriter.Write(sendSource2);
                }

                if (sendReadsCompletedFrame)
                {
                    WriteFrame(FrameType.StreamReadsCompleted, stream.Id, encode: null);
                }

                await _duplexConnectionWriter.FlushAsync(writeCts.Token).ConfigureAwait(false);
            }
            while (!source1.IsEmpty || !source2.IsEmpty); // Loop until there's no data left to send.
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Debug.Assert(_isClosed);
            throw new IceRpcException(_peerCloseError ?? IceRpcError.OperationAborted, _closedMessage);
        }

        return new FlushResult(isCanceled: false, isCompleted: false);

        void EncodeStreamFrameHeader(ulong streamId, long size, bool lastStreamFrame)
        {
            var encoder = new SliceEncoder(_duplexConnectionWriter, SliceEncoding.Slice2);
            encoder.EncodeFrameType(
                !lastStreamFrame ? FrameType.Stream :
                FrameType.StreamLast);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encoder.EncodeVarUInt62(streamId);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos + size), sizePlaceholder);
        }
    }

    internal void ThrowIfClosed()
    {
        lock (_mutex)
        {
            if (_isClosed)
            {
                throw new IceRpcException(_peerCloseError ?? IceRpcError.ConnectionAborted, _closedMessage);
            }
        }
    }

    private ValueTask<SemaphoreLock> AcquireWriteLockAsync(CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            // Make sure the connection is not being closed or closed when we acquire the semaphore.
            if (_isClosed)
            {
                throw new IceRpcException(_peerCloseError ?? IceRpcError.ConnectionAborted, _closedMessage);
            }
            return _writeSemaphore.AcquireAsync(cancellationToken);
        }
    }

    private void AddStream(ulong id, SlicStream stream)
    {
        lock (_mutex)
        {
            if (_isClosed)
            {
                throw new IceRpcException(_peerCloseError ?? IceRpcError.ConnectionAborted, _closedMessage);
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

    private void Close(Exception exception, string closeMessage, IceRpcError? peerCloseError = null)
    {
        lock (_mutex)
        {
            if (_isClosed)
            {
                return;
            }
            _isClosed = true;
            _closedMessage = closeMessage;
            _peerCloseError = peerCloseError;
            if (_streamSemaphoreWaitCount == 0)
            {
                _streamSemaphoreWaitClosed.SetResult();
            }
        }

        // Cancel pending CreateStreamAsync, AcceptStreamAsync and writes on the connection.
        _closedCts.Cancel();
        _acceptStreamChannel.Writer.TryComplete(exception);

        // Close streams.
        foreach (SlicStream stream in _streams.Values)
        {
            stream.Close(exception);
        }
    }

    private void DecodeParameters(IDictionary<ParameterKey, IList<byte>> parameters)
    {
        int? peerPacketMaxSize = null;
        int? peerPauseWriterThreshold = null;
        foreach ((ParameterKey key, IList<byte> buffer) in parameters)
        {
            switch (key)
            {
                case ParameterKey.MaxBidirectionalStreams:
                {
                    int value = DecodeParamValue(buffer);
                    if (value > 0)
                    {
                        _bidirectionalStreamSemaphore = new SemaphoreSlim(value, value);
                    }
                    break;
                }
                case ParameterKey.MaxUnidirectionalStreams:
                {
                    int value = DecodeParamValue(buffer);
                    if (value > 0)
                    {
                        _unidirectionalStreamSemaphore = new SemaphoreSlim(value, value);
                    }
                    break;
                }
                case ParameterKey.IdleTimeout:
                {
                    _peerIdleTimeout = TimeSpan.FromMilliseconds(DecodeParamValue(buffer));
                    if (_peerIdleTimeout == TimeSpan.Zero)
                    {
                        throw new InvalidDataException(
                            "The IdleTimeout Slic connection parameter is invalid, it must be greater than 0 s.");
                    }
                    break;
                }
                case ParameterKey.PacketMaxSize:
                {
                    peerPacketMaxSize = DecodeParamValue(buffer);
                    if (peerPacketMaxSize < 1024)
                    {
                        throw new InvalidDataException(
                            "The PacketMaxSize Slic connection parameter is invalid, it must be greater than 1KB.");
                    }
                    break;
                }
                case ParameterKey.PauseWriterThreshold:
                {
                    peerPauseWriterThreshold = DecodeParamValue(buffer);
                    if (peerPauseWriterThreshold < 1024)
                    {
                        throw new InvalidDataException(
                            "The PauseWriterThreshold Slic connection parameter is invalid, it must be greater than 1KB.");
                    }
                    break;
                }
                // Ignore unsupported parameter.
            }
        }

        if (peerPacketMaxSize is null)
        {
            throw new InvalidDataException(
                "The peer didn't send the required PacketMaxSize Slic connection parameter.");
        }
        else
        {
            PeerPacketMaxSize = peerPacketMaxSize.Value;
        }

        if (peerPauseWriterThreshold is null)
        {
            throw new InvalidDataException(
                "The peer didn't send the required PauseWriterThreshold Slic connection parameter.");
        }
        else
        {
            PeerPauseWriterThreshold = peerPauseWriterThreshold.Value;
        }

        // all parameter values are currently integers in the range 0..Int32Max encoded as varuint62.
        static int DecodeParamValue(IList<byte> buffer)
        {
            // The IList<byte> decoded by the Slice engine is backed by an array
            ulong value = SliceEncoding.Slice2.DecodeBuffer(
                new ReadOnlySequence<byte>((byte[])buffer),
                (ref SliceDecoder decoder) => decoder.DecodeVarUInt62());
            try
            {
                return checked((int)value);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("The value is out of the varuint32 accepted range.", exception);
            }
        }
    }

    private Dictionary<ParameterKey, IList<byte>> EncodeParameters()
    {
        var parameters = new List<KeyValuePair<ParameterKey, IList<byte>>>
        {
            // Required parameters.
            EncodeParameter(ParameterKey.PacketMaxSize, (ulong)_packetMaxSize),
            EncodeParameter(ParameterKey.PauseWriterThreshold, (ulong)PauseWriterThreshold)
        };

        // Optional parameters.
        if (_localIdleTimeout != Timeout.InfiniteTimeSpan)
        {
            parameters.Add(EncodeParameter(ParameterKey.IdleTimeout, (ulong)_localIdleTimeout.TotalMilliseconds));
        }
        if (_maxBidirectionalStreams > 0)
        {
            parameters.Add(EncodeParameter(ParameterKey.MaxBidirectionalStreams, (ulong)_maxBidirectionalStreams));
        }
        if (_maxUnidirectionalStreams > 0)
        {
            parameters.Add(EncodeParameter(ParameterKey.MaxUnidirectionalStreams, (ulong)_maxUnidirectionalStreams));
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

    private Task ReadFrameAsync(FrameType type, int size, ulong? streamId, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case FrameType.Close:
            {
                return ReadCloseFrameAsync(size, cancellationToken);
            }
            case FrameType.Ping:
            {
                lock (_mutex)
                {
                    // Send a new pong frame if the previous frame was sent and the connection is not closed or being
                    // close. The check for _isClosed ensures _pongTask is not reassigned once the connection is closed.
                    if (_pongTask.IsCompleted && !_isClosed)
                    {
                        // Send back a pong frame.
                        _pongTask = SendPongFrameAsync();
                    }
                }
                return Task.CompletedTask;
            }
            case FrameType.Pong:
            {
                // Nothing to do, the duplex connection reader keeps track of the last activity time.
                return Task.CompletedTask;
            }
            case FrameType.Stream:
            case FrameType.StreamLast:
            {
                return ReadStreamDataFrameAsync(type, size, streamId, cancellationToken);
            }
            case FrameType.StreamConsumed:
            {
                return ReadStreamFrameAsync(
                    size,
                    streamId,
                    (ref SliceDecoder decoder) => new StreamConsumedBody(ref decoder),
                    (stream, frame) => stream.ReceivedConsumedFrame(frame),
                    cancellationToken);
            }
            case FrameType.StreamReset:
            {
                return ReadStreamFrameAsync(
                    size,
                    streamId,
                    (ref SliceDecoder decoder) => new StreamResetBody(ref decoder),
                    (stream, frame) =>
                    {
                        stream.ReceivedResetFrame(frame);
                    },
                    cancellationToken);
            }
            case FrameType.StreamStopSending:
            {
                return ReadStreamFrameAsync(
                    size,
                    streamId,
                    (ref SliceDecoder decoder) => new StreamStopSendingBody(ref decoder),
                    (stream, frame) => stream.ReceivedStopSendingFrame(frame),
                    cancellationToken);
            }
            case FrameType.StreamReadsCompleted:
            {
                Debug.Assert(streamId is not null);
                if (size > 0)
                {
                    throw new InvalidDataException(
                        "Received invalid Slic stream reads completed frame, frame too large.");
                }

                if (_streams.TryGetValue(streamId.Value, out SlicStream? stream))
                {
                    stream.ReceivedReadsCompletedFrame();
                }
                return Task.CompletedTask;
            }
            default:
            {
                throw new InvalidDataException($"Received unexpected Slic frame '{type}'.");
            }
        }

        async Task ReadCloseFrameAsync(int size, CancellationToken cancellationToken)
        {
            CloseBody closeBody = await ReadFrameBodyAsync(
                size,
                (ref SliceDecoder decoder) => new CloseBody(ref decoder),
                cancellationToken).ConfigureAwait(false);

            lock (_mutex)
            {
                // If close is not already initiated, close the connection.
                _closeTask ??= PerformCloseAsync(closeBody.ApplicationErrorCode);
            }
            await _closeTask.ConfigureAwait(false);

            async Task PerformCloseAsync(ulong errorCode)
            {
                await Task.Yield(); // Exit mutex lock

                IceRpcError? peerCloseError = errorCode switch
                {
                    (ulong)MultiplexedConnectionCloseError.NoError => IceRpcError.ConnectionClosedByPeer,
                    (ulong)MultiplexedConnectionCloseError.Refused => IceRpcError.ConnectionRefused,
                    (ulong)MultiplexedConnectionCloseError.ServerBusy => IceRpcError.ServerBusy,
                    (ulong)MultiplexedConnectionCloseError.Aborted => IceRpcError.ConnectionAborted,
                    _ => null
                };

                if (peerCloseError is null)
                {
                    Close(
                        new IceRpcException(IceRpcError.ConnectionAborted),
                        $"The connection was closed by the peer with an unknown application error code: '{errorCode}'",
                        IceRpcError.ConnectionAborted);
                }
                else
                {
                    Close(
                        new IceRpcException(peerCloseError.Value),
                        "The connection was closed by the peer.",
                        peerCloseError);
                }

                // The server-side of the duplex connection is only shutdown once the client-side is shutdown. When
                // using TCP, this ensures that the server TCP connection won't end-up in the TIME_WAIT state on the
                // server-side.
                if (!IsServer)
                {
                    using SemaphoreLock _ = await _writeSemaphore.AcquireAsync(cancellationToken).ConfigureAwait(false);
                    _duplexConnectionWriter.Shutdown();
                }
            }
        }

        async Task<T> ReadFrameBodyAsync<T>(int size, DecodeFunc<T> decodeFunc, CancellationToken cancellationToken)
        {
            Debug.Assert(size > 0);

            ReadOnlySequence<byte> buffer = await _duplexConnectionReader.ReadAtLeastAsync(size, cancellationToken)
                .ConfigureAwait(false);

            if (buffer.Length > size)
            {
                buffer = buffer.Slice(0, size);
            }

            T decodedFrame = SliceEncoding.Slice2.DecodeBuffer(buffer, decodeFunc);
            _duplexConnectionReader.AdvanceTo(buffer.End);
            return decodedFrame;
        }

        async Task ReadStreamFrameAsync<T>(
            int size,
            ulong? streamId,
            DecodeFunc<T> decodeFunc,
            Action<SlicStream, T> streamAction,
            CancellationToken cancellationToken)
        {
            if (streamId is null)
            {
                throw new InvalidDataException("Received stream frame without stream ID.");
            }

            T frame = await ReadFrameBodyAsync(size, decodeFunc, cancellationToken).ConfigureAwait(false);
            if (_streams.TryGetValue(streamId.Value, out SlicStream? stream))
            {
                streamAction(stream, frame);
            }
        }

        async Task SendPongFrameAsync()
        {
            try
            {
                await SendFrameAsync(FrameType.Pong, encode: null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected if the connection was closed.
            }
            catch (IceRpcException)
            {
                // Expected if the connection failed.
            }
            catch (Exception exception)
            {
                Debug.Fail($"pong task failed with an unexpected exception: {exception}");
                throw;
            }
        }
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
                lock (_mutex)
                {
                    if (_isClosed)
                    {
                        return null;
                    }
                    else
                    {
                        // The duplex transport ReadAsync call returned an empty buffer. This indicates a peer
                        // connection abort.
                        throw new IceRpcException(IceRpcError.ConnectionAborted);
                    }
                }
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
        try
        {
            while (true)
            {
                (FrameType Type, int Size, ulong? StreamId)? header = await ReadFrameHeaderAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (header is null)
                {
                    // The peer has shut down the duplex connection.
                    break;
                }

                await ReadFrameAsync(header.Value.Type, header.Value.Size, header.Value.StreamId, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (IsServer)
            {
                // The server-side of the duplex connection is only shutdown once the client-side is shutdown. When
                // using TCP, this ensures that the server TCP connection won't end-up in the TIME_WAIT state on the
                // server-side.
                using SemaphoreLock _ = await _writeSemaphore.AcquireAsync(_disposedCts.Token).ConfigureAwait(false);
                _duplexConnectionWriter.Shutdown();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected, DisposeAsync was called.
        }
        catch (IceRpcException exception)
        {
            Close(exception, "The connection was lost.", IceRpcError.ConnectionAborted);
            throw;
        }
        catch (InvalidDataException exception)
        {
            var rpcException = new IceRpcException(
                IceRpcError.IceRpcError,
                "The connection was aborted by a Slic protocol error.",
                exception);
            Close(rpcException, rpcException.Message, IceRpcError.IceRpcError);
            throw rpcException;
        }
        catch (Exception exception)
        {
            Debug.Fail($"The read frames task completed due to an unhandled exception: {exception}");
            Close(exception, "The connection was lost.", IceRpcError.ConnectionAborted);
            throw;
        }
    }

    private async Task ReadStreamDataFrameAsync(
        FrameType type,
        int size,
        ulong? streamId,
        CancellationToken cancellationToken)
    {
        if (streamId is null)
        {
            throw new InvalidDataException("Received stream frame without stream ID.");
        }

        bool endStream = type == FrameType.StreamLast;
        bool isRemote = streamId % 2 == (IsServer ? 0ul : 1ul);
        bool isBidirectional = streamId % 4 < 2;

        if (!isBidirectional && !isRemote)
        {
            throw new InvalidDataException(
                "Received unexpected Slic stream frame on local unidirectional stream.");
        }
        else if (size == 0 && !endStream)
        {
            throw new InvalidDataException(
                "Received invalid Slic stream frame, received 0 bytes without end of stream.");
        }

        int readSize = 0;
        if (_streams.TryGetValue(streamId.Value, out SlicStream? stream))
        {
            // Let the stream receive the data.
            readSize = await stream.ReceivedStreamFrameAsync(
                size,
                endStream,
                cancellationToken).ConfigureAwait(false);
        }
        else if (isRemote && !IsKnownRemoteStream(streamId.Value, isBidirectional))
        {
            // Create a new stream if the remote stream is unknown.

            if (size == 0)
            {
                throw new InvalidDataException("Received empty Slic stream frame on new stream.");
            }

            if (isBidirectional)
            {
                if (_bidirectionalStreamCount == _maxBidirectionalStreams)
                {
                    throw new IceRpcException(
                        IceRpcError.IceRpcError,
                        $"The maximum bidirectional stream count {_maxBidirectionalStreams} was reached.");
                }
                Interlocked.Increment(ref _bidirectionalStreamCount);
            }
            else
            {
                if (_unidirectionalStreamCount == _maxUnidirectionalStreams)
                {
                    throw new IceRpcException(
                        IceRpcError.IceRpcError,
                        $"The maximum unidirectional stream count {_maxUnidirectionalStreams} was reached");
                }
                Interlocked.Increment(ref _unidirectionalStreamCount);
            }

            // Accept the new remote stream. The stream is queued on the channel reader. The caller of AcceptStreamAsync
            // is responsible for disposing the stream TODO: Cache SlicStream
            stream = new SlicStream(this, isBidirectional, remote: true);

            try
            {
                AddStream(streamId.Value, stream);

                // Let the stream receive the data.
                readSize = await stream.ReceivedStreamFrameAsync(
                    size,
                    endStream,
                    cancellationToken).ConfigureAwait(false);

                // Queue the new stream only if it read the full size (otherwise, it has been shutdown).
                if (readSize == size)
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

                        // The exception given to ChannelWriter.Complete(Exception? exception) is the
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
                Debug.Assert(stream.ReadsCompleted && stream.WritesCompleted);
            }
        }

        if (readSize < size)
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
                    size - readSize,
                    cancellationToken).ConfigureAwait(false);

            pipe.Writer.Complete();
            pipe.Reader.Complete();
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

    private void StartStream(SlicStream stream)
    {
        if (stream.WritesCompleted)
        {
            throw new InvalidOperationException("Cannot start a stream whose writes are already completed");
        }

        // The _nextBidirectionalId and _nextUnidirectionalId field can be safely updated below, they are protected by
        // the write semaphore.

        if (stream.IsBidirectional)
        {
            if (stream.ReadsCompleted)
            {
                throw new InvalidOperationException(
                    "Cannot start a bidirectional stream whose reads are already completed");
            }

            AddStream(_nextBidirectionalId, stream);
            _nextBidirectionalId += 4;
        }
        else
        {
            AddStream(_nextUnidirectionalId, stream);
            _nextUnidirectionalId += 4;
        }
    }

    private void WriteFrame(FrameType frameType, ulong? streamId, EncodeAction? encode)
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
    }
}
