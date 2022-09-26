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
                throw new InvalidOperationException("stream ID isn't allocated yet");
            }
            return id;
        }

        set
        {
            Debug.Assert(_id == ulong.MaxValue);
            Thread.VolatileWrite(ref _id, value);
        }
    }

    public PipeReader Input => IsRemote || IsBidirectional ?
        _inputPipeReader :
        throw new InvalidOperationException($"can't get {nameof(Input)} on unidirectional local stream");

    /// <inheritdoc/>
    public bool IsBidirectional { get; }

    /// <inheritdoc/>
    public bool IsRemote => _id != ulong.MaxValue && _id % 2 == (_connection.IsServer ? 0ul : 1ul);

    /// <inheritdoc/>
    public bool IsStarted => Thread.VolatileRead(ref _id) != ulong.MaxValue;

    public PipeWriter Output => !IsRemote || IsBidirectional ?
        _outputPipeWriter :
        throw new InvalidOperationException($"can't get {nameof(Output)} on unidirectional remote stream");

    public Task ReadsClosed => _readsClosedCompletionSource.Task;

    public Task WritesClosed => _writesClosedCompletionSource.Task;

    internal bool IsShutdown => ReadsCompleted && WritesCompleted;

    internal bool ReadsCompleted => _state.HasFlag(State.ReadsCompleted);

    internal bool WritesCompleted => _state.HasFlag(State.WritesCompleted);

    private readonly SlicConnection _connection;
    private ulong _id = ulong.MaxValue;
    private readonly SlicPipeReader _inputPipeReader;
    private readonly SlicPipeWriter _outputPipeWriter;
    private volatile int _sendCredit = int.MaxValue;
    // The semaphore is used when flow control is enabled to wait for additional send credit to be available.
    private readonly AsyncSemaphore _sendCreditSemaphore = new(1, 1);
    private readonly TaskCompletionSource _readsClosedCompletionSource = new();
    private readonly TaskCompletionSource _writesClosedCompletionSource = new();
    private int _state;

    public void Abort(Exception completeException)
    {
        if (TrySetReadsClosed(completeException))
        {
            _inputPipeReader.Abort(completeException);
        }
        if (TrySetWritesClosed(completeException))
        {
            _outputPipeWriter.Abort(completeException);
        }
    }

    internal SlicStream(SlicConnection connection, bool bidirectional, bool remote)
    {
        _connection = connection;
        _sendCredit = _connection.PeerPauseWriterThreshold;

        _inputPipeReader = new SlicPipeReader(
            this,
            _connection.Pool,
            _connection.MinSegmentSize,
            _connection.ResumeWriterThreshold,
            _connection.PauseWriterThreshold);

        _outputPipeWriter = new SlicPipeWriter(this, _connection.Pool, _connection.MinSegmentSize);

        IsBidirectional = bidirectional;
        if (!IsBidirectional)
        {
            if (remote)
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
    }

    internal void AbortRead(Exception? exception)
    {
        if (!IsStarted)
        {
            // If the stream is not started or the connection aborted, there's no need to send a stop sending frame.
            TrySetReadsClosed(exception: null);
        }
        else if (!ReadsCompleted)
        {
            _ = SendStopSendingFrameAndCompleteReadsAsync();
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
                    new StreamStopSendingBody(_connection.ErrorCodeConverter.ToErrorCode(exception)).Encode,
                    default).ConfigureAwait(false);
            }
            catch
            {
                // Ignore.
            }

            if (!IsRemote)
            {
                // Complete reads after sending the stop sending frame to ensure the peer's stream max count is
                // decreased before it receives a frame for a new stream.
                TrySetReadsClosed(exception: null);
            }
        }
    }

    internal void AbortWrite(Exception? exception)
    {
        if (!IsStarted)
        {
            // If the stream is not started or the connection aborted, there's no need to send a reset frame.
            TrySetWritesClosed(exception: null);
        }
        else if (!WritesCompleted)
        {
            _ = SendResetFrameAndCompleteWritesAsync();
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
                    new StreamResetBody(_connection.ErrorCodeConverter.ToErrorCode(exception)).Encode,
                    default).ConfigureAwait(false);
            }
            catch
            {
                // Ignore.
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
            throw new TransportException(TransportErrorCode.InternalError, "invalid flow control credit increase");
        }
    }

    internal ValueTask<int> ReceivedStreamFrameAsync(int size, bool endStream, CancellationToken cancellationToken) =>
        ReadsCompleted ? new(0) : _inputPipeReader.ReceivedStreamFrameAsync(size, endStream, cancellationToken);

    internal void ReceivedResetFrame(ulong errorCode)
    {
        if (!IsBidirectional && !IsRemote)
        {
            throw new TransportException(
                TransportErrorCode.InternalError,
                "received reset frame on local unidirectional stream");
        }

        Exception? exception = _connection.ErrorCodeConverter.FromErrorCode(errorCode);
        if (TrySetReadsClosed(exception))
        {
            _inputPipeReader.Abort(exception);
        }
    }

    internal void ReceivedStopSendingFrame(ulong errorCode)
    {
        if (!IsBidirectional && IsRemote)
        {
            throw new TransportException(
                TransportErrorCode.InternalError,
                "received stop sending on remote unidirectional stream");
        }

        Exception? exception = _connection.ErrorCodeConverter.FromErrorCode(errorCode);
        if (TrySetWritesClosed(exception))
        {
            _outputPipeWriter.Abort(exception);
        }
    }

    internal ValueTask<FlushResult> SendStreamFrameAsync(
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        CancellationToken cancellationToken) =>
        _connection.SendStreamFrameAsync(this, source1, source2, endStream, cancellationToken);

    internal void SendStreamConsumed(int size) =>
        _ = _connection.SendFrameAsync(
            stream: this,
            FrameType.StreamConsumed,
            new StreamConsumedBody((ulong)size).Encode,
            CancellationToken.None).AsTask();

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
                // The stream reads and writes are completed, it's time to shutdown the stream.
                if (IsStarted)
                {
                    Shutdown();
                }
            }
            return true;
        }
        else
        {
            return false;
        }

        void Shutdown()
        {
            // Release connection stream count or semaphore for this stream and remove it from the connection.
            _connection.ReleaseStream(this);

            // Local bidirectional streams are released from the connection when the StreamLast or StreamReset frame is
            // received. Since a remote unidirectional stream doesn't send stream frames, we have to send a
            // UnidirectionalStreamReleased frame here to ensure the peer's stream is released. It's important to send
            // the frame after the ReleaseStream call above to prevent a race condition where the peer could start a new
            // unidirectional stream before the local counter is decreased.
            if (IsRemote && !IsBidirectional)
            {
                try
                {
                    _ = _connection.SendFrameAsync(
                        stream: this,
                        FrameType.UnidirectionalStreamReleased,
                        encode: null,
                        default).AsTask();
                }
                catch
                {
                    // Ignore, the connection is closed.
                }
            }
        }
    }

    [Flags]
    private enum State : int
    {
        ReadsCompleted = 1,
        WritesCompleted = 2,
    }
}
