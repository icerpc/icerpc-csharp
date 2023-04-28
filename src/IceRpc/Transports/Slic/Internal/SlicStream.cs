// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>The stream implementation for Slic. The stream implementation implements flow control to ensure data
/// isn't buffered indefinitely if the application doesn't consume it. Buffering and flow control are only enable
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

    private readonly SlicConnection _connection;
    private ulong _id = ulong.MaxValue;
    private readonly SlicPipeReader? _inputPipeReader;
    private readonly SlicPipeWriter? _outputPipeWriter;
    private readonly TaskCompletionSource _readsClosedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _readsCompletionPending;
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

    internal void CompleteReads(ulong? errorCode = null)
    {
        if (!ReadsCompleted || _readsCompletionPending)
        {
            if (IsStarted && (errorCode is not null || IsRemote))
            {
                _readsCompletionPending = true;
                _ = PerformCompleteReadsAsync(errorCode);
            }
            else
            {
                TrySetReadsCompleted();
            }
        }

        async Task PerformCompleteReadsAsync(ulong? errorCode)
        {
            try
            {
                if (IsRemote)
                {
                    // If it's a remote stream, we complete reads before sending the reads completed or stop sending
                    // frame. This ensures that the stream max count is decreased before the peer receives the frame.
                    TrySetReadsCompleted();
                }

                if (errorCode is not null)
                {
                    await _connection.SendStreamFrameAsync(
                        stream: this,
                        FrameType.StreamStopSending,
                        new StreamStopSendingBody(errorCode.Value).Encode).ConfigureAwait(false);
                }
                else if (IsRemote)
                {
                    // The stream reads completed frame is only sent for remote streams.
                    await _connection.SendStreamFrameAsync(
                        stream: this,
                        FrameType.StreamReadsCompleted,
                        encode: null).ConfigureAwait(false);
                }
                // When completing reads for a local stream, there's no need to notify the peer. The peer already
                // completed writes after sending the last stream frame.

                if (!IsRemote)
                {
                    // We can now complete reads to permit a new stream to be started. The peer will receive the stop
                    // sending or reads completed frame before the new stream sends a stream frame.
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

    internal void CompleteWrites(ulong? errorCode = null)
    {
        if (!WritesCompleted && !_writesCompletionPending)
        {
            if (IsStarted)
            {
                _writesCompletionPending = true;
                _ = PerformCompleteWritesAsync(errorCode);
            }
            else
            {
                TrySetWritesCompleted();
            }
        }

        async Task PerformCompleteWritesAsync(ulong? errorCode)
        {
            try
            {
                if (IsRemote)
                {
                    // If it's a remote stream, we complete writes before sending the stream last frame or reset frame
                    // to ensure the stream max count is decreased before the peer receives the frame.
                    TrySetWritesCompleted();
                }

                if (errorCode is null)
                {
                    await _connection.SendStreamFrameAsync(
                        this,
                        ReadOnlySequence<byte>.Empty,
                        ReadOnlySequence<byte>.Empty,
                        endStream: true,
                        default).ConfigureAwait(false);

                    // If the stream is a local stream, writes are not completed until the stream reads completed frame
                    // or stop sending frame is received from the peer.
                }
                else
                {
                    await _connection.SendStreamFrameAsync(
                        stream: this,
                        FrameType.StreamReset,
                        new StreamResetBody(applicationErrorCode: 0).Encode).ConfigureAwait(false);

                    if (!IsRemote)
                    {
                        // We can now complete writes to permit a new stream to be started. The peer will receive the
                        // reset frame before the new stream sends a stream frame.
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
                    new StreamConsumedBody((ulong)size).Encode).ConfigureAwait(false);
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
        if (endStream)
        {
            _writesCompletionPending = true;
        }
        return _connection.SendStreamFrameAsync(this, source1, source2, endStream, cancellationToken);
    }

    internal void SentLastStreamFrame()
    {
        if (IsRemote)
        {
            TrySetWritesCompleted();
        }
        // Writes will be completed when the peer's sends the stop sending or reads completed frame.

        _writesClosedTcs.TrySetResult();
    }

    internal void ThrowIfConnectionClosed() => _connection.ThrowIfClosed();

    internal bool TrySetReadsCompleted()
    {
        if (TrySetState(State.ReadsCompleted))
        {
            _readsClosedTcs.TrySetResult();
            _readsCompletionPending = false;
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
            _writesCompletionPending = false;
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
            if ((state.HasFlag(State.ReadsCompleted) || state.HasFlag(State.WritesCompleted)) &&
                newState.HasFlag(State.ReadsCompleted | State.WritesCompleted))
            {
                // The stream reads and writes are completed, it's time to release the stream.
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
