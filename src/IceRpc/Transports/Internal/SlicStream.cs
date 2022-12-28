// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

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
    private volatile int _sendCredit = int.MaxValue;
    // The semaphore is used when flow control is enabled to wait for additional send credit to be available.
    private readonly AsyncSemaphore _sendCreditSemaphore = new(1, 1);
    private Task? _sendStreamConsumedFrameTask;
    private int _state;
    private readonly TaskCompletionSource _writesClosedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal SlicStream(SlicConnection connection, bool bidirectional, bool remote)
    {
        _connection = connection;
        _sendCredit = _connection.PeerPauseWriterThreshold;

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
            _inputPipeReader = new SlicPipeReader(
                this,
                _connection.Pool,
                _connection.MinSegmentSize,
                _connection.ResumeWriterThreshold,
                _connection.PauseWriterThreshold);
        }

        if (!IsRemote || IsBidirectional)
        {
            _outputPipeWriter = new SlicPipeWriter(this, _connection.Pool, _connection.MinSegmentSize);
        }
    }

    internal void Abort(IceRpcException completeException)
    {
        if (TrySetReadsCompleted())
        {
            _inputPipeReader?.Abort(completeException);
        }
        if (TrySetWritesCompleted())
        {
            _outputPipeWriter?.Abort(completeException);
        }
    }

    internal void AbortRead(ulong errorCode) => CompleteReads(errorCode);

    internal void AbortWrite(ulong errorCode) => CompleteWrites(errorCode);

    internal async ValueTask<int> AcquireSendCreditAsync(CancellationToken cancellationToken)
    {
        // Acquire the semaphore to ensure flow control allows sending additional data. It's important to acquire
        // the semaphore before checking _sendCredit. The semaphore acquisition will block if we can't send
        // additional data (_sendCredit == 0). Acquiring the semaphore ensures that we are allowed to send
        // additional data and _sendCredit can be used to figure out the size of the next packet to send.
        await _sendCreditSemaphore.EnterAsync(cancellationToken).ConfigureAwait(false);
        return _sendCredit;
    }

    internal void CompleteReads(ulong? errorCode = null)
    {
        Console.Error.WriteLine($"sending complete reads {Id}-{IsRemote} {ReadsCompleted} {errorCode ?? 255}");
        if (!ReadsCompleted)
        {
            if (IsRemote || !IsStarted)
            {
                // Complete reads before sending the stop sending or reads completed frame to ensure the stream max
                // count is decreased before the peer receives the frame.
                TrySetReadsCompleted();
            }
            // Reads will be completed when the peer's sends the reset or last stream frame.

            _ = SendCompleteReadsFrameAsync(errorCode);

            // Locally, reads are considered closed even if they will really only be closed once the peer's sends the
            // reset or last stream frame.
            _readsClosedTcs.TrySetResult();
        }

        async Task SendCompleteReadsFrameAsync(ulong? errorCode)
        {
            try
            {
                if (errorCode is null)
                {
                    await _connection.SendStreamFrameAsync(
                        stream: this,
                        FrameType.StreamReadsCompleted,
                        encode: null,
                        default).ConfigureAwait(false);
                }
                else
                {
                    await _connection.SendStreamFrameAsync(
                        stream: this,
                        FrameType.StreamStopSending,
                        new StreamStopSendingBody(errorCode.Value).Encode,
                        default).ConfigureAwait(false);
                }
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail(
                    $"Failed to send Slic stream complete reads frame due to an unhandled exception: {exception}");
                throw;
            }
        }
    }

    internal void CompleteWrites(ulong? errorCode = null)
    {
        Console.Error.WriteLine($"sending complete writes {Id}-{IsRemote} {WritesCompleted} {errorCode}");

        if (!WritesCompleted)
        {
            if (IsRemote)
            {
                // Complete writes before sending the reset or last stream frame to ensure the stream max count
                // is decreased before the peer receives the frame.
                TrySetWritesCompleted();
            }
            // Writes will be completed when the peer's sends the stop sending or reads completed frame.

            _ = SendCompleteWritesFrameAsync(errorCode);

            // Locally, writes are considered closed even if they will really only be closed once the peer's sends the
            // stop sending or reads completed frame.
            _writesClosedTcs.TrySetResult();
        }

        async Task SendCompleteWritesFrameAsync(ulong? errorCode)
        {
            try
            {
                if (errorCode is null)
                {
                    await _connection.SendStreamFrameAsync(
                        this,
                        ReadOnlySequence<byte>.Empty,
                        ReadOnlySequence<byte>.Empty,
                        endStream: true,
                        default).ConfigureAwait(false);
                }
                else
                {
                    await _connection.SendStreamFrameAsync(
                        stream: this,
                        FrameType.StreamReset,
                        new StreamResetBody(applicationErrorCode: 0).Encode,
                        default).ConfigureAwait(false);
                }
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail(
                    $"Failed to send Slic stream complete writes frame due to an unhandled exception: {exception}");
                throw;
            }
        }
    }

    internal void ConsumeSendCredit(int consumed)
    {
        // Decrease the size of remaining data that we are allowed to send. If all the credit is consumed,
        // _sendCredit will be 0 and we don't release the semaphore to prevent further sends. The semaphore will be
        // released once the stream receives a StreamConsumed frame.
        int sendCredit = Interlocked.Add(ref _sendCredit, -consumed);
        if (sendCredit > 0)
        {
            _sendCreditSemaphore.Release();
        }
        Debug.Assert(sendCredit >= 0);
    }

    internal ValueTask FillBufferWriterAsync(
        IBufferWriter<byte> bufferWriter,
        int byteCount,
        CancellationToken cancellationToken) =>
        _connection.FillBufferWriterAsync(bufferWriter, byteCount, cancellationToken);

    internal void ReceivedConsumedFrame(int size)
    {
        int newValue = Interlocked.Add(ref _sendCredit, size);
        if (newValue == size)
        {
            Debug.Assert(_sendCreditSemaphore.Count == 0);
            _sendCreditSemaphore.Release();
        }
        else if (newValue > _connection.PeerPauseWriterThreshold)
        {
            // The peer is trying to increase the credit to a value which is larger than what it is allowed to.
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "The consumed frame size is trying to increase the credit to a value larger than allowed.");
        }
    }

    internal ValueTask<int> ReceivedStreamFrameAsync(int size, bool endStream, CancellationToken cancellationToken)
    {
        Debug.Assert(_inputPipeReader is not null);
        return ReadsCompleted ? new(0) : _inputPipeReader.ReceivedStreamFrameAsync(size, endStream, cancellationToken);
    }

    internal void ReceivedReadsCompletedFrame(ulong? errorCode)
    {
        Console.Error.WriteLine($"received reads completed frame {Id}-{IsRemote} {WritesCompleted} {errorCode ?? 255}");
        // When peer reads are completed, we can complete writes if not already completed.
        if (TrySetWritesCompleted())
        {
            if (errorCode is null or 0ul)
            {
                // Write operations will return a completed flush result regardless of wether or not the peer aborted
                // reads with the 0ul error code or completed reads.
                _outputPipeWriter?.Abort(exception: null);
            }
            else
            {
                // The peer aborted reads with unknown application error code.
                _outputPipeWriter?.Abort(new IceRpcException(
                    IceRpcError.IceRpcError,
                    $"The peer aborted stream reads with an unknown application error code: '{errorCode}'"));
            }
        }
    }

    internal void ReceivedWritesCompletedFrame(ulong? errorCode)
    {
        Console.Error.WriteLine($"received reads completed frame {Id}-{IsRemote} {ReadsCompleted} {errorCode ?? 255}");
        // When peer writes are completed, we can complete reads if not already completed.
        if (TrySetReadsCompleted())
        {
            if (errorCode is null)
            {
                // Read operations will return a completed ReadResult.
                _inputPipeReader?.Abort(exception: null);
            }
            else if (errorCode == 0ul)
            {
                // Read operations will return a TruncatedData if the peer aborted writes.
                _inputPipeReader?.Abort(new IceRpcException(IceRpcError.TruncatedData));
            }
            else
            {
                // The peer aborted writes with unknown application error code.
                _outputPipeWriter?.Abort(new IceRpcException(
                    IceRpcError.IceRpcError,
                    $"The peer aborted stream writes with an unknown application error code: '{errorCode}'"));
            }
        }
    }

    internal ValueTask<FlushResult> SendStreamFrameAsync(
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        CancellationToken cancellationToken)
    {
        if (IsRemote)
        {
            // Complete writes before sending the reset or last stream frame to ensure the stream max count
            // is decreased before the peer receives the frame.
            TrySetWritesCompleted();
        }
        // Writes will be completed when the peer's sends the stop sending or reads completed frame.

        return _connection.SendStreamFrameAsync(this, source1, source2, endStream, cancellationToken);
    }

    internal void SendStreamConsumed(int size)
    {
        Task previousSendStreamConsumedFrameTask = _sendStreamConsumedFrameTask ?? Task.CompletedTask;
        _sendStreamConsumedFrameTask = SendStreamConsumedFrameAsync();

        async Task SendStreamConsumedFrameAsync()
        {
            try
            {
                // First wait for the sending of the previous stream consumed task to complete.
                await previousSendStreamConsumedFrameTask.ConfigureAwait(false);

                // Send the stream consumed frame.
                await _connection.SendStreamFrameAsync(
                    stream: this,
                    FrameType.StreamConsumed,
                    new StreamConsumedBody((ulong)size).Encode,
                    CancellationToken.None).ConfigureAwait(false);
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

    internal bool TrySetWritesCompleted()
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
