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

    public Task InputClosed => _readsClosedCompletionSource.Task;

    public Task OutputClosed => _writesClosedCompletionSource.Task;

    internal bool IsShutdown => ReadsCompleted && WritesCompleted;

    internal bool ReadsCompleted => _state.HasFlag(State.ReadsCompleted);

    internal bool WritesCompleted => _state.HasFlag(State.WritesCompleted);

    private readonly SlicConnection _connection;
    private ulong _id = ulong.MaxValue;
    private readonly SlicPipeReader? _inputPipeReader;
    private readonly SlicPipeWriter? _outputPipeWriter;
    private readonly TaskCompletionSource _readsClosedCompletionSource = new();
    private volatile int _sendCredit = int.MaxValue;
    // The semaphore is used when flow control is enabled to wait for additional send credit to be available.
    private readonly AsyncSemaphore _sendCreditSemaphore = new(1, 1);
    private Task? _sendReadsClosedFrameTask;
    private Task? _sendStreamConsumedFrameTask;
    private Task? _sendUnidirectionalStreamReleaseFrameTask;
    private Task? _sendWritesClosedFrameTask;
    private int _state;
    private readonly TaskCompletionSource _writesClosedCompletionSource = new();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Task.WhenAll(
                _sendReadsClosedFrameTask ?? Task.CompletedTask,
                _sendWritesClosedFrameTask ?? Task.CompletedTask,
                _sendStreamConsumedFrameTask ?? Task.CompletedTask,
                _sendUnidirectionalStreamReleaseFrameTask ?? Task.CompletedTask).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Debug.Fail($"Slic stream disposal failed due to an unhandled exception: {exception}");
        }

        // Abort reads and writes if reads and writes are not closed and ensure that the input pipe reader and output
        // pipe writer operations fail with OperationAborted.
        var abortException = new IceRpcException(IceRpcError.OperationAborted);
        if (!ReadsCompleted)
        {
            AbortRead();
            _inputPipeReader?.Abort(abortException);
        }
        if (!WritesCompleted)
        {
            AbortWrite();
            _outputPipeWriter?.Abort(abortException);
        }
    }

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
                TrySetWritesClosed(exception: null);
            }
            else
            {
                // Read-side of local unidirectional stream is marked as completed.
                TrySetReadsClosed(exception: null);
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

    internal void Abort(Exception completeException)
    {
        if (TrySetReadsClosed(completeException))
        {
            _inputPipeReader?.Abort(completeException);
        }
        if (TrySetWritesClosed(completeException))
        {
            _outputPipeWriter?.Abort(completeException);
        }
    }

    internal void AbortRead()
    {
        if (!IsStarted)
        {
            // If the stream is not started or the connection aborted, there's no need to send a stop sending frame.
            TrySetReadsClosed(exception: null);
        }
        else if (IsBidirectional && !ReadsCompleted)
        {
            // SlicPipeReader.Complete guarantees that AbortRead is called at most once.
            Debug.Assert(_sendReadsClosedFrameTask is null);
            _sendReadsClosedFrameTask = SendStopSendingFrameAndCompleteReadsAsync();
        }

        async Task SendStopSendingFrameAndCompleteReadsAsync()
        {
            if (IsRemote)
            {
                // Complete reads before sending the stop sending frame to ensure the stream max count is decreased
                // before the peer receives the frame.
                TrySetReadsClosed(exception: null);
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
                // Ignore connection failures. Other exceptions will be caught by DisposeAsync.
            }

            if (!IsRemote)
            {
                // Complete reads after sending the stop sending frame to ensure the peer's stream max count is
                // decreased before it receives a frame for a new stream.
                TrySetReadsClosed(exception: null);
            }
        }
    }

    internal void AbortWrite()
    {
        if (!IsStarted)
        {
            // If the stream is not started or the connection aborted, there's no need to send a reset frame.
            TrySetWritesClosed(exception: null);
        }
        else if (!WritesCompleted)
        {
            // SlicPipeWriter.Complete guarantees that AbortRead is called at most once.
            Debug.Assert(_sendWritesClosedFrameTask is null);
            _sendWritesClosedFrameTask = SendResetFrameAndCompleteWritesAsync();
        }

        async Task SendResetFrameAndCompleteWritesAsync()
        {
            if (IsRemote)
            {
                // Complete writes before sending the reset frame to ensure the stream max count is decreased before
                // the peer receives the frame.
                TrySetWritesClosed(exception: null);
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
                // Ignore connection failures. Other exceptions will be caught by DisposeAsync.
            }

            if (!IsRemote)
            {
                // Complete writes after sending the reset frame to ensure the peer's stream max count is decreased
                // before it receives a frame for a new stream.
                TrySetWritesClosed(exception: null);
            }
        }
    }

    internal async ValueTask<int> AcquireSendCreditAsync(CancellationToken cancellationToken)
    {
        // Acquire the semaphore to ensure flow control allows sending additional data. It's important to acquire
        // the semaphore before checking _sendCredit. The semaphore acquisition will block if we can't send
        // additional data (_sendCredit == 0). Acquiring the semaphore ensures that we are allowed to send
        // additional data and _sendCredit can be used to figure out the size of the next packet to send.
        await _sendCreditSemaphore.EnterAsync(cancellationToken).ConfigureAwait(false);
        return _sendCredit;
    }

    internal void CompleteUnidirectionalStreamReads()
    {
        // SlicPipeReader.Complete guarantees that AbortRead is called at most once.
        Debug.Assert(_sendUnidirectionalStreamReleaseFrameTask is null);

        // Notify the peer that reads are completed. The connection will release the unidirectional stream semaphore
        // and allow opening a new unidirectional stream if the maximum unidirectional count was reached.
        _sendUnidirectionalStreamReleaseFrameTask = SendUnidirectionalStreamReleaseAsync();

        async Task SendUnidirectionalStreamReleaseAsync()
        {
            // Complete reads before sending the unidirectional stream released frame to ensure the stream max count is
            // decreased before the peer receives the frame.
            TrySetReadsClosed(exception: null);

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
                // Ignore connection failures. Other exceptions will be caught by DisposeAsync.
            }
        }
    }

    internal void CompleteWrites()
    {
        if (!IsStarted)
        {
            // If the stream is not started or the connection aborted, there's no need to send a StreamLast frame.
            TrySetWritesClosed(exception: null);
        }
        else if (!WritesCompleted)
        {
            // SlicPipeWriter.Complete guarantees that AbortRead is called at most once.
            Debug.Assert(_sendWritesClosedFrameTask is null);
            _sendWritesClosedFrameTask = SendStreamLastFrameAsync();
        }

        async Task SendStreamLastFrameAsync()
        {
            try
            {
                await _connection.SendStreamFrameAsync(
                    this,
                    ReadOnlySequence<byte>.Empty,
                    ReadOnlySequence<byte>.Empty,
                    endStream: true,
                    default).ConfigureAwait(false);
            }
            catch (IceRpcException)
            {
                // Ignore connection failures. Other exception will be caught by DisposeAsync.
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

    internal void ReceivedResetFrame()
    {
        if (!IsBidirectional && !IsRemote)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "Received unexpected Slic reset frame on local unidirectional stream.");
        }

        var exception = new IceRpcException(IceRpcError.TruncatedData);
        if (TrySetReadsClosed(exception))
        {
            _inputPipeReader?.Abort(exception);
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

        if (TrySetWritesClosed(exception: null))
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

        if (TrySetWritesClosed(null))
        {
            _outputPipeWriter?.Abort(null);
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
                // Ignore connection failures. Other exceptions will be caught by DisposeAsync.
            }
        }
    }

    internal bool TrySetReadsClosed(Exception? exception)
    {
        if (TrySetState(State.ReadsCompleted))
        {
            if (exception is null)
            {
                _readsClosedCompletionSource.SetResult();
            }
            else
            {
                _readsClosedCompletionSource.SetException(exception);
            }
            return true;
        }
        else
        {
            return false;
        }
    }

    internal bool TrySetWritesClosed(Exception? exception)
    {
        if (TrySetState(State.WritesCompleted))
        {
            if (exception is null)
            {
                _writesClosedCompletionSource.SetResult();
            }
            else
            {
                _writesClosedCompletionSource.SetException(exception);
            }
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
