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
    /// <summary>Gets a value indicating whether or not this is the server-side of the connection.</summary>
    internal bool IsServer { get; }

    /// <summary>Gets the minimum size of the segment requested from <see cref="Pool" />.</summary>
    internal int MinSegmentSize { get; }

    internal int PauseWriterThreshold { get; }

    /// <summary>Gets the maximum size of packets accepted by the peer.</summary>
    internal int PeerPacketMaxSize { get; private set; }

    // TODO: replace with a window size property
    internal int PeerPauseWriterThreshold { get; private set; }

    /// <summary>Gets the <see cref="MemoryPool{T}" /> used for obtaining memory buffers.</summary>
    internal MemoryPool<byte> Pool { get; }

    // TODO: replace with a window size property
    internal int ResumeWriterThreshold { get; }

    private readonly Channel<IMultiplexedStream> _acceptStreamChannel;
    private int _bidirectionalStreamCount;
    private SemaphoreSlim? _bidirectionalStreamSemaphore;
    private readonly CancellationToken _closedCancellationToken;
    private readonly CancellationTokenSource _closedCts = new();
    private string? _closedMessage;
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
    private int _pendingPongCount;
    private Task? _readFramesTask;

    private readonly ConcurrentDictionary<ulong, SlicStream> _streams = new();
    private int _streamSemaphoreWaitCount;
    private readonly TaskCompletionSource _streamSemaphoreWaitClosed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _unidirectionalStreamCount;
    private SemaphoreSlim? _unidirectionalStreamSemaphore;

    // This is only set for server connections to ensure that _duplexConnectionWriter.Write is not called after
    // _duplexConnectionWriter.Shutdown. This can occur if the client-side of the connection sends the close frame
    // followed by the shutdown of the duplex connection and if CloseAsync is called at the same time on the server
    // connection.
    private bool _writerIsShutdown;
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
                        await WriteConnectionFrameAsync(
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
                    await WriteConnectionFrameAsync(
                        FrameType.InitializeAck,
                        new InitializeAckBody(EncodeParameters()).Encode,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Write the Initialize frame.
                    await WriteConnectionFrameAsync(
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
            // _pendingPongCount can be < 0 if an unexpected pong is received. If it's the case, the connection is being
            // torn down and there's no point in sending a ping frame.
            if (Interlocked.Increment(ref _pendingPongCount) > 0)
            {
                _ = PingAsync();
            }

            async Task PingAsync()
            {
                try
                {
                    // For now, the Ping frame payload is just a long which is always set to 0. In the future, it could
                    // be a ping frame type value if the ping frame is used for different purpose (e.g: a KeepAlive or
                    // RTT ping frame type).
                    await WriteConnectionFrameAsync(
                        FrameType.Ping,
                        new PingBody(0L).Encode,
                        _closedCancellationToken).ConfigureAwait(false);
                }
                catch (IceRpcException)
                {
                    // Expected if the connection is closed.
                }
                catch (Exception exception)
                {
                    Debug.Fail($"The Slic keep alive timer failed with an unexpected exception: {exception}");
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
        }

        // Calling DisposeAsync while CloseAsync is pending is not allowed. We can safely assume that _writeSemaphore is
        // not disposed and there's no need to create a linked token source with _disposedCts.Token.

        bool waitForWriterShutdown = false;
        if (TryClose(new IceRpcException(IceRpcError.OperationAborted), "The connection was closed."))
        {
            using SemaphoreLock _ = await _writeSemaphore.AcquireAsync(cancellationToken).ConfigureAwait(false);

            // The duplex connection writer of a server connection might already be shutdown (_writerIsShutdown=true) if
            // the client-side sent the Close frame and shut down the duplex connection. This doesn't apply to the
            // client-side since the server-side doesn't shutdown the duplex connection writer after sending the Close
            // frame.
            if (!IsServer || !_writerIsShutdown)
            {
                WriteFrame(FrameType.Close, streamId: null, new CloseBody((ulong)closeError).Encode);
                if (IsServer)
                {
                    _duplexConnectionWriter.Flush();
                }
                else
                {
                    // The sending of the client-side Close frame is followed by the shutdown of the duplex connection.
                    // For TCP, it's important to always shutdown the connection on the client-side first to avoid
                    // TIME_WAIT states on the server-side.
                    _duplexConnectionWriter.Shutdown();
                    waitForWriterShutdown = true;
                }
            }
        }

        if (waitForWriterShutdown)
        {
            // Wait for the writer task completion outside the semaphore lock.
            await _duplexConnectionWriter.WriterTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // Now, wait for the peer to close the write side of the connection, which will terminate the read frames task.
        Debug.Assert(_readFramesTask is not null);
        await _readFramesTask.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        TryClose(new IceRpcException(IceRpcError.OperationAborted), "The connection was disposed.");

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
                    _streamSemaphoreWaitClosed.Task).ConfigureAwait(false);
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
                // Prevents unobserved task exceptions.
                await _acceptStreamChannel.Reader.Completion.ConfigureAwait(false);
            }
            catch
            {
            }

            // Wait for tasks to release the write semaphore before disposing the duplex connection writer.
            using (await _writeSemaphore.AcquireAsync(CancellationToken.None).ConfigureAwait(false))
            {
                await _duplexConnectionWriter.DisposeAsync().ConfigureAwait(false);
            }
            _duplexConnectionReader.Dispose();
            _duplexConnection.Dispose();

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

    /// <summary>Fills the given writer with stream data received on the connection.</summary>
    /// <param name="bufferWriter">The destination buffer writer.</param>
    /// <param name="byteCount">The amount of stream data to read.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    internal ValueTask FillBufferWriterAsync(
        IBufferWriter<byte> bufferWriter,
        int byteCount,
        CancellationToken cancellationToken) =>
        _duplexConnectionReader.FillBufferWriterAsync(bufferWriter, byteCount, cancellationToken);

    /// <summary>Releases a stream from the connection. The connection stream count is decremented and if this is a
    /// client allow a new stream to be started.</summary>
    /// <param name="stream">The released stream.</param>
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

    /// <summary>Throws the connection closure exception if the connection is already closed.</summary>
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

    /// <summary>Writes a connection frame.</summary>
    /// <param name="frameType">The frame type.</param>
    /// <param name="encode">The action to encode the frame.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    internal async Task WriteConnectionFrameAsync(
        FrameType frameType,
        EncodeAction? encode,
        CancellationToken cancellationToken)
    {
        Debug.Assert(frameType < FrameType.Stream);

        using SlicDuplexConnectionWriterLock _ = await AcquireWriterLockAsync(cancellationToken).ConfigureAwait(false);
        WriteFrame(frameType, streamId: null, encode);
    }

    /// <summary>Writes a stream frame.</summary>
    /// <param name="stream">The stream to write the frame for.</param>
    /// <param name="frameType">The frame type.</param>
    /// <param name="encode">The action to encode the frame.</param>
    /// <param name="writeReadsClosedFrame"><see langword="true" /> if a <see cref="FrameType.StreamReadsClosed" />
    /// frame should be written after the stream frame.</param>
    /// <remarks>This method is called by streams and might be called on a closed connection. The connection might
    /// also be closed concurrently while it's in progress.</remarks>
    internal async Task WriteStreamFrameAsync(
        SlicStream stream,
        FrameType frameType,
        EncodeAction? encode,
        bool writeReadsClosedFrame)
    {
        // Ensure that this method is called for any FrameType.StreamXxx frame type except FrameType.Stream.
        Debug.Assert(frameType >= FrameType.StreamLast && stream.IsStarted);

        using SlicDuplexConnectionWriterLock _ =
            await AcquireWriterLockAsync(_closedCancellationToken).ConfigureAwait(false);
        WriteFrame(frameType, stream.Id, encode);
        if (writeReadsClosedFrame)
        {
            WriteFrame(FrameType.StreamReadsClosed, stream.Id, encode: null);
        }
        if (frameType == FrameType.StreamLast)
        {
            // Notify the stream that the last stream frame is considered sent at this point. This will close
            // writes on the stream and allow the stream to be released if reads are also closed.
            stream.WroteLastStreamFrame();
        }
    }

    /// <summary>Writes a stream data frame.</summary>
    /// <param name="stream">The stream to write the frame for.</param>
    /// <param name="source1">The first stream frame data source.</param>
    /// <param name="source2">The second stream frame data source.</param>
    /// <param name="endStream"><see langword="true" /> to write a <see cref="FrameType.StreamLast" /> frame and
    /// <see langword="false" /> to write a <see cref="FrameType.Stream" /> frame.</param>
    /// <param name="writeReadsClosedFrame"><see langword="true" /> if a <see cref="FrameType.StreamReadsClosed" />
    /// frame should be written after the stream frame.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <remarks>This method is called by streams and might be called on a closed connection. The connection might
    /// also be closed concurrently while it's in progress.</remarks>
    internal async ValueTask<FlushResult> WriteStreamDataFrameAsync(
        SlicStream stream,
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        bool writeReadsClosedFrame,
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

                using SlicDuplexConnectionWriterLock _ =
                    await AcquireWriterLockAsync(writeCts.Token).ConfigureAwait(false);

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
                    stream.WroteLastStreamFrame();
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

                if (writeReadsClosedFrame)
                {
                    WriteFrame(FrameType.StreamReadsClosed, stream.Id, encode: null);
                }
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
            encoder.EncodeFrameType(!lastStreamFrame ? FrameType.Stream : FrameType.StreamLast);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encoder.EncodeVarUInt62(streamId);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos + size), sizePlaceholder);
        }
    }

    private async Task<SlicDuplexConnectionWriterLock> AcquireWriterLockAsync(CancellationToken cancellationToken)
    {
        Task waitTask;
        lock (_mutex)
        {
            // Make sure the connection is not being closed or closed when we acquire the semaphore.
            if (_isClosed)
            {
                throw new IceRpcException(_peerCloseError ?? IceRpcError.ConnectionAborted, _closedMessage);
            }
            waitTask = _writeSemaphore.WaitAsync(cancellationToken);
        }
        await waitTask.ConfigureAwait(false);
        return new SlicDuplexConnectionWriterLock(this);
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
        if (type >= FrameType.Stream && streamId is null)
        {
            throw new InvalidDataException("Received stream frame without stream ID.");
        }

        switch (type)
        {
            case FrameType.Close:
            {
                return ReadCloseFrameAsync(size, cancellationToken);
            }
            case FrameType.Ping:
            {
                return ReadPingFrameAndWritePongFrameAsync(size, cancellationToken);
            }
            case FrameType.Pong:
            {
                return ReadPongFrameAsync(size, cancellationToken);
            }
            case FrameType.Stream:
            case FrameType.StreamLast:
            {
                return ReadStreamDataFrameAsync(type, size, streamId!.Value, cancellationToken);
            }
            case FrameType.StreamConsumed:
            {
                return ReadStreamConsumedFrameAsync(size, streamId!.Value, cancellationToken);
            }
            case FrameType.StreamReadsClosed:
            {
                if (_streams.TryGetValue(streamId!.Value, out SlicStream? stream))
                {
                    stream.ReceivedReadsClosedFrame();
                }
                return Task.CompletedTask;
            }
            case FrameType.StreamWritesClosed:
            {
                if (_streams.TryGetValue(streamId!.Value, out SlicStream? stream))
                {
                    stream.ReceivedWritesClosedFrame();
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

            IceRpcError? peerCloseError = closeBody.ApplicationErrorCode switch
            {
                (ulong)MultiplexedConnectionCloseError.NoError => IceRpcError.ConnectionClosedByPeer,
                (ulong)MultiplexedConnectionCloseError.Refused => IceRpcError.ConnectionRefused,
                (ulong)MultiplexedConnectionCloseError.ServerBusy => IceRpcError.ServerBusy,
                (ulong)MultiplexedConnectionCloseError.Aborted => IceRpcError.ConnectionAborted,
                _ => null
            };

            bool notAlreadyClosed;
            if (peerCloseError is null)
            {
                notAlreadyClosed = TryClose(
                    new IceRpcException(IceRpcError.ConnectionAborted),
                    $"The connection was closed by the peer with an unknown application error code: '{closeBody.ApplicationErrorCode}'",
                    IceRpcError.ConnectionAborted);
            }
            else
            {
                notAlreadyClosed = TryClose(
                    new IceRpcException(peerCloseError.Value),
                    "The connection was closed by the peer.",
                    peerCloseError);
            }

            // The server-side of the duplex connection is only shutdown once the client-side is shutdown. When
            // using TCP, this ensures that the server TCP connection won't end-up in the TIME_WAIT state on the
            // server-side.
            if (notAlreadyClosed && !IsServer)
            {
                // DisposeAsync waits for the reads frames task to complete before disposing the semaphore.
                using (await _writeSemaphore.AcquireAsync(cancellationToken).ConfigureAwait(false))
                {
                    _duplexConnectionWriter.Shutdown();
                }
                // Wait for the writer task completion outside the semaphore lock.
                await _duplexConnectionWriter.WriterTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        async Task ReadPingFrameAndWritePongFrameAsync(int size, CancellationToken cancellationToken)
        {
            // Read the ping frame.
            PingBody pingBody = await ReadFrameBodyAsync(
                size,
                (ref SliceDecoder decoder) => new PingBody(ref decoder),
                cancellationToken).ConfigureAwait(false);

            // Return a pong frame with the ping payload.
            await WriteConnectionFrameAsync(
                FrameType.Pong,
                new PongBody(pingBody.Payload).Encode,
                cancellationToken).ConfigureAwait(false);
        }

        async Task ReadPongFrameAsync(int size, CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref _pendingPongCount) >= 0)
            {
                // Ensure the pong frame payload value is expected.

                PongBody pongBody = await ReadFrameBodyAsync(
                    size,
                    (ref SliceDecoder decoder) => new PongBody(ref decoder),
                    cancellationToken).ConfigureAwait(false);

                // For now, we only send a 0 payload value.
                if (pongBody.Payload != 0L)
                {
                    throw new InvalidDataException($"Received {nameof(FrameType.Pong)} with unexpected payload.");
                }
            }
            else
            {
                // If not waiting for a pong frame, this pong frame is unexpected.
                throw new InvalidDataException($"Received an unexpected {nameof(FrameType.Pong)} frame.");
            }
        }

        async Task ReadStreamConsumedFrameAsync(int size, ulong streamId, CancellationToken cancellationToken)
        {
            StreamConsumedBody frame = await ReadFrameBodyAsync(
                size,
                (ref SliceDecoder decoder) => new StreamConsumedBody(ref decoder),
                cancellationToken).ConfigureAwait(false);
            if (_streams.TryGetValue(streamId, out SlicStream? stream))
            {
                stream.ReceivedConsumedFrame(frame);
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
                Debug.Assert(_isClosed);

                // The server-side of the duplex connection is only shutdown once the client-side is shutdown. When
                // using TCP, this ensures that the server TCP connection won't end-up in the TIME_WAIT state on the
                // server-side.

                // DisposeAsync waits for the reads frames task to complete before disposing the semaphore.
                using (await _writeSemaphore.AcquireAsync(cancellationToken).ConfigureAwait(false))
                {
                    _duplexConnectionWriter.Shutdown();

                    // Make sure that CloseAsync doesn't call Write on the writer if it's called shortly after the peer
                    // shutdown its side of the connection (which triggers ReadFrameHeaderAsync to return null).
                    _writerIsShutdown = true;
                }

                // Wait for the writer task completion outside the semaphore lock.
                await _duplexConnectionWriter.WriterTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected, DisposeAsync was called.
        }
        catch (IceRpcException exception)
        {
            TryClose(exception, "The connection was lost.", IceRpcError.ConnectionAborted);
            throw;
        }
        catch (InvalidDataException exception)
        {
            var rpcException = new IceRpcException(
                IceRpcError.IceRpcError,
                "The connection was aborted by a Slic protocol error.",
                exception);
            TryClose(rpcException, rpcException.Message, IceRpcError.IceRpcError);
            throw rpcException;
        }
        catch (Exception exception)
        {
            Debug.Fail($"The read frames task completed due to an unhandled exception: {exception}");
            TryClose(exception, "The connection was lost.", IceRpcError.ConnectionAborted);
            throw;
        }
    }

    private async Task ReadStreamDataFrameAsync(
        FrameType type,
        int size,
        ulong streamId,
        CancellationToken cancellationToken)
    {
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

        if (!_streams.TryGetValue(streamId, out SlicStream? stream) &&
            isRemote &&
            !IsKnownRemoteStream(streamId, isBidirectional))
        {
            // Create a new remote stream.

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

            // The stream is registered with the connection and queued on the channel. The caller of AcceptStreamAsync
            // is responsible for cleaning up the stream.
            stream = new SlicStream(this, isBidirectional, remote: true);

            try
            {
                AddStream(streamId, stream);

                try
                {
                    await _acceptStreamChannel.Writer.WriteAsync(
                        stream,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException exception)
                {
                    // The exception given to ChannelWriter.Complete(Exception? exception) is the InnerException.
                    Debug.Assert(exception.InnerException is not null);
                    throw ExceptionUtil.Throw(exception.InnerException);
                }
            }
            catch (IceRpcException)
            {
                // The two methods above throw IceRpcException if the connection has been closed (either by CloseAsync
                // or because the close frame was received). We cleanup up the stream but don't throw to not abort the
                // reading. The connection graceful closure still needs to read on the connection to figure out when the
                // peer shuts down the duplex connection.
                Debug.Assert(_isClosed);
                stream.Input.Complete();
                if (isBidirectional)
                {
                    stream.Output.Complete();
                }
            }
        }

        bool isDataConsumed = false;

        if (stream is not null)
        {
            // Let the stream consume the stream frame data.
            isDataConsumed = await stream.ReceivedStreamFrameAsync(
                size,
                endStream,
                cancellationToken).ConfigureAwait(false);
        }

        if (!isDataConsumed)
        {
            // The stream (if any) didn't consume the data. Read and ignore the data using a helper pipe.
            var pipe = new Pipe(
                new PipeOptions(
                    pool: Pool,
                    pauseWriterThreshold: 0,
                    minimumSegmentSize: MinSegmentSize,
                    useSynchronizationContext: false));

            await _duplexConnectionReader.FillBufferWriterAsync(
                    pipe.Writer,
                    size,
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

    private void ReleaseWriterLock()
    {
        _duplexConnectionWriter.Flush();
        _writeSemaphore.Release();
    }

    private bool TryClose(Exception exception, string closeMessage, IceRpcError? peerCloseError = null)
    {
        lock (_mutex)
        {
            if (_isClosed)
            {
                return false;
            }
            _isClosed = true;
            _closedMessage = closeMessage;
            _peerCloseError = peerCloseError;
            if (_streamSemaphoreWaitCount == 0)
            {
                _streamSemaphoreWaitClosed.SetResult();
            }
        }

        // Cancel pending CreateStreamAsync, AcceptStreamAsync and WriteStreamFrameAsync operations.
        _closedCts.Cancel();
        _acceptStreamChannel.Writer.TryComplete(exception);

        // Close streams.
        foreach (SlicStream stream in _streams.Values)
        {
            stream.Close(exception);
        }

        return true;
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

    private readonly struct SlicDuplexConnectionWriterLock : IDisposable
    {
        private readonly SlicConnection _connection;

        public void Dispose() => _connection.ReleaseWriterLock();

        internal SlicDuplexConnectionWriterLock(SlicConnection connection) => _connection = connection;
    }
}
