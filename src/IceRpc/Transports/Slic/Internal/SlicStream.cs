// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>The stream implementation for Slic. The stream implementation implements flow control to ensure data
/// isn't buffered indefinitely if the application doesn't consume it. Buffering and flow control are only enabled
/// when sending multiple Slic packet or if the Slic packet size exceeds the peer packet maximum size.</summary>
internal class SlicStream : IMultiplexedStream
{
    public ulong Id
    {
        get
        {
            ulong id = Thread.VolatileRead(ref _id);
            if (id == ulong.MaxValue)
            {
                throw new InvalidOperationException("The stream ID isn't allocated yet.");
            }
            return id;
        }

        set
        {
            Debug.Assert(_id == ulong.MaxValue);
            Thread.VolatileWrite(ref _id, value);
        }
    }

    public PipeReader Input =>
        _inputPipeReader ?? throw new InvalidOperationException("A local unidirectional stream has no Input.");

    /// <inheritdoc/>
    public bool IsBidirectional { get; }

    /// <inheritdoc/>
    public bool IsRemote { get; }

    /// <inheritdoc/>
    public bool IsStarted => Thread.VolatileRead(ref _id) != ulong.MaxValue;

    public PipeWriter Output =>
        _outputPipeWriter ?? throw new InvalidOperationException("A remote unidirectional stream has no Output.");

    public Task ReadsClosed => _readsClosedTcs.Task;

    public Task WritesClosed => _writesClosedTcs.Task;

    internal bool ReadsCompleted => _state.HasFlag(State.ReadsCompleted);

    internal bool WritesCompleted => _state.HasFlag(State.WritesCompleted);

    private bool _completeReadsOnWriteCompletion;
    private readonly SlicConnection _connection;
    private ulong _id = ulong.MaxValue;
    private readonly SlicPipeReader? _inputPipeReader;
    // This mutex protects _readsCompletionPending, _writesCompletionPending, _completeReadsOnWriteCompletion.
    private readonly object _mutex = new();
    private readonly SlicPipeWriter? _outputPipeWriter;
    private readonly TaskCompletionSource _readsClosedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _readsCompletionPending;
    // FlagEnumExtensions operations are used to update the state. These operations are atomic and don't require mutex
    // locking.
    private int _state;
    private readonly TaskCompletionSource _writesClosedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _writesCompletionPending;

    internal SlicStream(SlicConnection connection, bool bidirectional, bool remote)
    {
        _connection = connection;

        IsBidirectional = bidirectional;
        IsRemote = remote;

        if (!IsBidirectional)
        {
            if (IsRemote)
            {
                // Write-side of remote unidirectional stream is marked as completed.
                TrySetWritesCompleted();
            }
            else
            {
                // Read-side of local unidirectional stream is marked as completed.
                TrySetReadsCompleted();
            }
        }

        if (IsRemote || IsBidirectional)
        {
            _inputPipeReader = new SlicPipeReader(this, _connection);
        }

        if (!IsRemote || IsBidirectional)
        {
            _outputPipeWriter = new SlicPipeWriter(this, _connection);
        }
    }

    internal ValueTask<int> AcquireSendCreditAsync(CancellationToken cancellationToken) =>
        _outputPipeWriter!.AcquireSendCreditAsync(cancellationToken);

    internal void Close(Exception completeException)
    {
        if (TrySetReadsCompleted())
        {
            Debug.Assert(_inputPipeReader is not null);
            _inputPipeReader.CompleteReads(completeException);
        }
        if (TrySetWritesCompleted())
        {
            Debug.Assert(_outputPipeWriter is not null);
            _outputPipeWriter.CompleteWrites(completeException);
        }
    }

    /// <summary>This method completes the read-side of the stream. It's only called by SlicPipeReader methods and never
    /// called concurrently.</summary>
    /// <param name="errorCode">The error code. It's null if reads were completed after the StreamLast frame was
    /// consumed. It's non-null if the reader was completed with an exception or before the buffer data was
    /// consumed.</param>
    internal void CompleteReads(ulong? errorCode = null)
    {
        bool performCompleteReads = false;

        lock (_mutex)
        {
            if (IsStarted && !ReadsCompleted && !_readsCompletionPending)
            {
                if (errorCode is null && IsBidirectional && !WritesCompleted && !_writesCompletionPending && IsRemote)
                {
                    // As an optimization, if reads are completed once the buffered data is consumed but before writes
                    // are closed, we don't send the StreamReadsCompleted frame just yet. Instead, when writes are
                    // completed, CompleteWrites will bundle the sending of the StreamReadsCompleted with the sending of
                    // the StreamLast or StreamReset frame.
                    _completeReadsOnWriteCompletion = true;
                }
                else if (errorCode is not null || IsRemote)
                {
                    _readsCompletionPending = true;
                    performCompleteReads = true;
                }
            }
        }

        if (performCompleteReads)
        {
            _ = PerformCompleteReadsAsync(errorCode);
        }
        else
        {
            TrySetReadsCompleted();
        }

        async Task PerformCompleteReadsAsync(ulong? errorCode)
        {
            Debug.Assert(IsRemote || errorCode is not null);
            try
            {
                if (IsRemote)
                {
                    // If it's a remote stream, we complete writes before sending the StreamReadsCompleted or
                    // StreamStopSending frame to ensure _connection._bidirectionalStreamCount or
                    // _connection._unidirectionalStreamCount is decreased before the peer receives the frame. This is
                    // necessary to prevent a race condition where the peer could release the connection's bidirectional
                    // or unidirectional stream semaphore before this connection's stream count is actually decreased.
                    TrySetReadsCompleted();
                }

                if (errorCode is not null)
                {
                    await _connection.SendStreamFrameAsync(
                        stream: this,
                        FrameType.StreamStopSending,
                        new StreamStopSendingBody(errorCode.Value).Encode,
                        sendReadsCompletedFrame: false).ConfigureAwait(false);
                }
                else if (IsRemote)
                {
                    // The stream reads completed frame is only sent for remote streams to notify the local stream that
                    // the buffered data on the SlicPipeReader was consumed. Once the peer receives this notification,
                    // it can release the connection's bidirectional or unidirectional stream semaphore (if writes are
                    // also completed).
                    await _connection.SendStreamFrameAsync(
                        stream: this,
                        FrameType.StreamReadsCompleted,
                        encode: null,
                        sendReadsCompletedFrame: false).ConfigureAwait(false);
                }
                // When completing reads for a local stream, there's no need to notify the peer. The peer already
                // completed writes after sending the StreamLast or StreamReset frame.

                if (!IsRemote)
                {
                    // We can now complete reads to permit a new stream to be started. The peer will receive the
                    // StreamStopSending or StreamReadsCompleted frame before the new stream sends a Stream frame.
                    TrySetReadsCompleted();
                }
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail($"Failed to send frame from CompleteReads due to an unhandled exception: {exception}");
                throw;
            }
        }
    }

    /// <summary>This method completes the write-side of the stream. It's only called by SlicPipeWriter methods and
    /// never called concurrently.</summary>
    /// <param name="errorCode">The error code. It's null if the writer was completed without an exception, non-null
    /// otherwise.</param>
    internal void CompleteWrites(ulong? errorCode = null)
    {
        bool performCompleteWrites = false;
        bool sendReadsCompletedFrame = false;

        lock (_mutex)
        {
            if (IsStarted && !WritesCompleted && !_writesCompletionPending)
            {
                sendReadsCompletedFrame = _completeReadsOnWriteCompletion;
                _writesCompletionPending = true;
                performCompleteWrites = true;
            }
        }

        if (performCompleteWrites)
        {
            _ = PerformCompleteWritesAsync(errorCode);
        }
        else
        {
            TrySetWritesCompleted();
        }

        async Task PerformCompleteWritesAsync(ulong? errorCode)
        {
            try
            {
                if (IsRemote)
                {
                    // If it's a remote stream, we complete writes before sending the StreamLast or StreamReset frame to
                    // ensure _connection._bidirectionalStreamCount or _connection._unidirectionalStreamCount is
                    // decreased before the peer receives the frame. This is necessary to prevent a race condition where
                    // the peer could release the connection's bidirectional or unidirectional stream semaphore before
                    // this connection's stream count is actually decreased.
                    TrySetWritesCompleted();
                }

                if (errorCode is null)
                {
                    await _connection.SendStreamFrameAsync(
                        this,
                        ReadOnlySequence<byte>.Empty,
                        ReadOnlySequence<byte>.Empty,
                        endStream: true,
                        sendReadsCompletedFrame,
                        default).ConfigureAwait(false);

                    // If the stream is a local stream, writes are not completed until the StreamReadsCompleted or
                    // StreamStopSending frame is received from the peer. This ensures that the connection's
                    // bidirectional or unidirectional stream semaphore is released only once the peer consumed the
                    // buffered data.
                }
                else
                {
                    await _connection.SendStreamFrameAsync(
                        stream: this,
                        FrameType.StreamReset,
                        new StreamResetBody(applicationErrorCode: 0).Encode,
                        sendReadsCompletedFrame).ConfigureAwait(false);

                    if (!IsRemote)
                    {
                        // We can now complete writes to allow starting a new stream. Since the sending of frames is
                        // serialized over the connection, the peer will receive this StreamReset frame before the new
                        // stream sends StreamFrame frame.
                        TrySetWritesCompleted();
                    }
                }
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail($"Failed to send frame from CompleteWrites due to an unhandled exception: {exception}");
                throw;
            }
        }
    }

    internal void ConsumedSendCredit(int consumed) => _outputPipeWriter!.ConsumedSendCredit(consumed);

    internal ValueTask FillBufferWriterAsync(
        IBufferWriter<byte> bufferWriter,
        int byteCount,
        CancellationToken cancellationToken) =>
        _connection.FillBufferWriterAsync(bufferWriter, byteCount, cancellationToken);

    internal void ReceivedConsumedFrame(StreamConsumedBody frame)
    {
        int newSendCredit = _outputPipeWriter!.ReceivedConsumedFrame((int)frame.Size);

        // Ensure the peer is not trying to increase the credit to a value which is larger than what it is allowed to.
        if (newSendCredit > _connection.PeerPauseWriterThreshold)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "The consumed frame size is trying to increase the credit to a value larger than allowed.");
        }
    }

    internal void ReceivedReadsCompletedFrame()
    {
        if (IsRemote)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "Received invalid Slic stream reads completed frame, the stream is a remote stream.");
        }

        TrySetWritesCompleted();

        // Write operations will return a completed flush result regardless of wether or not the peer aborted reads with
        // the 0ul error code or completed reads.
        _outputPipeWriter?.CompleteWrites(exception: null);
    }

    internal void ReceivedResetFrame(StreamResetBody frame)
    {
        TrySetReadsCompleted();

        if (frame.ApplicationErrorCode == 0ul)
        {
            // Read operations will return a TruncatedData if the peer aborted writes.
            _inputPipeReader?.CompleteReads(new IceRpcException(IceRpcError.TruncatedData));
        }
        else
        {
            // The peer aborted writes with unknown application error code.
            _inputPipeReader?.CompleteReads(new IceRpcException(
                IceRpcError.TruncatedData,
                $"The peer aborted stream writes with an unknown application error code: '{frame.ApplicationErrorCode}'"));
        }
    }

    internal void ReceivedStopSendingFrame(StreamStopSendingBody frame)
    {
        TrySetWritesCompleted();

        if (frame.ApplicationErrorCode == 0ul)
        {
            // Write operations will return a completed flush result regardless of wether or not the peer aborted
            // reads with the 0ul error code or completed reads.
            _outputPipeWriter?.CompleteWrites(exception: null);
        }
        else
        {
            // The peer aborted reads with unknown application error code.
            _outputPipeWriter?.CompleteWrites(new IceRpcException(
                IceRpcError.TruncatedData,
                $"The peer aborted stream reads with an unknown application error code: '{frame.ApplicationErrorCode}'"));
        }
    }

    internal ValueTask<int> ReceivedStreamFrameAsync(int size, bool endStream, CancellationToken cancellationToken)
    {
        Debug.Assert(_inputPipeReader is not null);
        return ReadsCompleted ? new(0) : _inputPipeReader.ReceivedStreamFrameAsync(size, endStream, cancellationToken);
    }

    internal void SendStreamConsumed(int size)
    {
        _ = SendStreamConsumedFrameAsync();

        async Task SendStreamConsumedFrameAsync()
        {
            try
            {
                // Send the stream consumed frame.
                await _connection.SendStreamFrameAsync(
                    stream: this,
                    FrameType.StreamConsumed,
                    new StreamConsumedBody((ulong)size).Encode,
                    sendReadsCompletedFrame: false).ConfigureAwait(false);
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail($"Sending of Slic stream consumed frame failed due to an unhandled exception: {exception}");
                throw;
            }
        }
    }

    internal ValueTask<FlushResult> SendStreamFrameAsync(
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        CancellationToken cancellationToken)
    {
        bool sendReadsCompletedFrame = false;
        if (endStream)
        {
            lock (_mutex)
            {
                sendReadsCompletedFrame = _completeReadsOnWriteCompletion;
                _writesCompletionPending = true;
            }
        }

        return _connection.SendStreamFrameAsync(
            this,
            source1,
            source2,
            endStream,
            sendReadsCompletedFrame,
            cancellationToken);
    }

    internal void SentLastStreamFrame()
    {
        if (IsRemote)
        {
            TrySetWritesCompleted();
        }
        // For local local streams, writes will be completed only once the peer's sends the StreamStopSending or
        // StreamReadsCompleted frame (indicating that its buffered data was consumed).

        _writesClosedTcs.TrySetResult();
    }

    internal void ThrowIfConnectionClosed() => _connection.ThrowIfClosed();

    internal bool TrySetReadsCompleted()
    {
        if (TrySetState(State.ReadsCompleted))
        {
            _readsClosedTcs.TrySetResult();
            return true;
        }
        else
        {
            return false;
        }
    }

    private bool TrySetWritesCompleted()
    {
        if (TrySetState(State.WritesCompleted))
        {
            _writesClosedTcs.TrySetResult();
            return true;
        }
        else
        {
            return false;
        }
    }

    private bool TrySetState(State state)
    {
        if (_state.TrySetFlag(state, out int newState))
        {
            if (newState.HasFlag(State.ReadsCompleted | State.WritesCompleted))
            {
                // The stream reads and writes are completed, it's time to release the stream to either allow creating
                // or accepting a new stream.
                if (IsStarted)
                {
                    _connection.ReleaseStream(this);
                }
            }
            return true;
        }
        else
        {
            return false;
        }
    }

    [Flags]
    private enum State : int
    {
        ReadsCompleted = 1,
        WritesCompleted = 2,
    }
}
