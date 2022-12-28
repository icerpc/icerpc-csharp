// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>The stream implementation for Slic. The stream implementation implements flow control to ensure data
/// isn't buffered indefinitely if the application doesn't consume it. Buffering and flow control are only enable
/// when sending multiple Slic packet or if the Slic packet size exceeds the peer packet maximum size.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The _sendCreditSemaphore is disposed by TrySetWritesClosed")]
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
    private readonly SemaphoreSlim _sendCreditSemaphore = new(1, 1);
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
                TrySetWritesClosed();
            }
            else
            {
                // Read-side of local unidirectional stream is marked as completed.
                TrySetReadsClosed();
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
        if (TrySetReadsClosed())
        {
            _inputPipeReader?.Abort(completeException);
        }
        if (TrySetWritesClosed())
        {
            _outputPipeWriter?.Abort(completeException);
        }
    }

    internal void AbortRead()
    {
        if (!ReadsCompleted)
        {
            if (IsStarted && IsBidirectional)
            {
                // SlicPipeReader.Complete guarantees that AbortRead is called at most once.
                _ = SendStopSendingFrameAndCompleteReadsAsync();
            }
            else
            {
                TrySetReadsClosed();
            }
        }

        async Task SendStopSendingFrameAndCompleteReadsAsync()
        {
            if (IsRemote)
            {
                // Complete reads before sending the stop sending frame to ensure the stream max count is decreased
                // before the peer receives the frame.
                TrySetReadsClosed();
            }

            try
            {
                await _connection.SendFrameAsync(
                    stream: this,
                    FrameType.StreamStopSending,
                    new StreamStopSendingBody(applicationErrorCode: 0).Encode,
                    default).ConfigureAwait(false);
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail($"Sending of Slic stream stop sending frame due to an unhandled exception: {exception}");
                throw;
            }

            if (!IsRemote)
            {
                // Complete reads after sending the stop sending frame to ensure the peer's stream max count is
                // decreased before it receives a frame for a new stream.
                TrySetReadsClosed();
            }
        }
    }

    internal void AbortWrite()
    {
        if (!WritesCompleted)
        {
            if (IsStarted)
            {
                // SlicPipeWriter.Complete guarantees that AbortWrite is called at most once.
                _ = SendResetFrameAndCompleteWritesAsync();
            }
            else
            {
                TrySetWritesClosed();
            }
        }

        async Task SendResetFrameAndCompleteWritesAsync()
        {
            if (IsRemote)
            {
                // Complete writes before sending the reset frame to ensure the stream max count is decreased before
                // the peer receives the frame.
                TrySetWritesClosed();
            }

            try
            {
                await _connection.SendFrameAsync(
                    stream: this,
                    FrameType.StreamReset,
                    new StreamResetBody(applicationErrorCode: 0).Encode,
                    default).ConfigureAwait(false);
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail($"Sending of Slic stream reset frame failed due to an unhandled exception: {exception}");
                throw;
            }

            if (!IsRemote)
            {
                // Complete writes after sending the reset frame to ensure the peer's stream max count is decreased
                // before it receives a frame for a new stream.
                TrySetWritesClosed();
            }
        }
    }

    internal async ValueTask<int> AcquireSendCreditAsync(CancellationToken cancellationToken)
    {
        // Acquire the semaphore to ensure flow control allows sending additional data. It's important to acquire
        // the semaphore before checking _sendCredit. The semaphore acquisition will block if we can't send
        // additional data (_sendCredit == 0). Acquiring the semaphore ensures that we are allowed to send
        // additional data and _sendCredit can be used to figure out the size of the next packet to send.
        await _sendCreditSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return _sendCredit;
    }

    internal void CompleteWrites()
    {
        if (!WritesCompleted)
        {
            if (IsStarted)
            {
                // SlicPipeWriter.Complete guarantees that AbortRead is called at most once.
                _ = SendStreamLastFrameAsync();
            }
            else
            {
                TrySetWritesClosed();
            }
        }

        async Task SendStreamLastFrameAsync()
        {
            try
            {
                // SendStreamFrameAsync calls TrySetWritesClosed when appropriate.
                await _connection.SendStreamFrameAsync(
                    this,
                    ReadOnlySequence<byte>.Empty,
                    ReadOnlySequence<byte>.Empty,
                    endStream: true,
                    default).ConfigureAwait(false);
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail($"Sending of Slic stream last frame failed due to an unhandled exception: {exception}");
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
            Debug.Assert(_sendCreditSemaphore.CurrentCount == 0);
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

    internal void ReceivedResetFrame()
    {
        if (!IsBidirectional && !IsRemote)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "Received unexpected Slic reset frame on local unidirectional stream.");
        }

        if (TrySetReadsClosed())
        {
            _inputPipeReader?.Abort(new IceRpcException(IceRpcError.TruncatedData));
        }
    }

    internal void ReceivedStopSendingFrame()
    {
        if (!IsBidirectional && IsRemote)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "Received unexpected Slic stop sending frame on remote unidirectional stream.");
        }

        if (TrySetWritesClosed())
        {
            _outputPipeWriter?.Abort(exception: null);
        }
    }

    internal void ReceivedUnidirectionalStreamReleasedFrame()
    {
        if (IsBidirectional && IsRemote)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "Received unexpected Slic unidirectional stream released frame on remote bidirectional stream.");
        }

        if (TrySetWritesClosed())
        {
            _outputPipeWriter?.Abort(exception: null);
        }
    }

    internal ValueTask<FlushResult> SendStreamFrameAsync(
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        CancellationToken cancellationToken) =>
        _connection.SendStreamFrameAsync(this, source1, source2, endStream, cancellationToken);

    internal void SendStreamConsumed(int size)
    {
        Task previousSendStreamConsumedFrameTask = _sendStreamConsumedFrameTask ?? Task.CompletedTask;
        _sendStreamConsumedFrameTask = SendStreamLastFrameAsync();

        async Task SendStreamLastFrameAsync()
        {
            try
            {
                // First wait for the sending of the previous stream consumed task to complete.
                await previousSendStreamConsumedFrameTask.ConfigureAwait(false);

                // Send the stream consumed frame.
                await _connection.SendFrameAsync(
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

    internal bool TrySetReadsClosed()
    {
        if (TrySetState(State.ReadsCompleted))
        {
            if (IsRemote && !IsBidirectional)
            {
                CompleteUnidirectionalStreamReads();
            }
            _readsClosedTcs.SetResult();
            return true;
        }
        else
        {
            return false;
        }
    }

    internal bool TrySetWritesClosed()
    {
        if (TrySetState(State.WritesCompleted))
        {
            _writesClosedTcs.TrySetResult();
            _sendCreditSemaphore.Dispose();
            return true;
        }
        else
        {
            return false;
        }
    }

    private void CompleteUnidirectionalStreamReads()
    {
        Debug.Assert(ReadsCompleted);

        // Notify the peer that reads are completed. The connection will release the unidirectional stream semaphore
        // and allow opening a new unidirectional stream if the maximum unidirectional count was reached.
        _ = SendUnidirectionalStreamReleaseAsync();

        async Task SendUnidirectionalStreamReleaseAsync()
        {
            try
            {
                await _connection.SendFrameAsync(
                    stream: this,
                    FrameType.UnidirectionalStreamReleased,
                    encode: null,
                    default).ConfigureAwait(false);
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail($"Sending of Slic unidirectional stream released frame failed due to an unhandled exception: {exception}");
                throw;
            }
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
