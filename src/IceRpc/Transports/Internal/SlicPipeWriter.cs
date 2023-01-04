// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

#pragma warning disable CA1001 // Type owns disposable field(s) '_abortCts' but is not disposable
internal class SlicPipeWriter : ReadOnlySequencePipeWriter
#pragma warning restore CA1001
{
    private readonly CancellationTokenSource _abortCts = new(); // Disposed by Complete
    private IceRpcException? _exception;
    private readonly Pipe _pipe;
    private volatile int _sendCredit = int.MaxValue;
    // The semaphore is used when flow control is enabled to wait for additional send credit to be available.
    private readonly SemaphoreSlim _sendCreditSemaphore = new(1, 1);
    private int _state;
    private readonly SlicStream _stream;

    public override void Advance(int bytes)
    {
        CheckIfCompleted();
        _pipe.Writer.Advance(bytes);
    }

    // SlicPipeWriter does not support this method: the IceRPC core does not need it. And when the application code
    // installs a payload writer interceptor, this interceptor should never call it on "next".
    public override void CancelPendingFlush() => throw new NotSupportedException();

    public override void Complete(Exception? exception = null)
    {
        if (_state.TrySetFlag(State.Completed))
        {
            if (!_stream.WritesCompleted && exception is null && _pipe.Writer.UnflushedBytes > 0)
            {
                throw new InvalidOperationException(
                    $"Completing a {nameof(SlicPipeWriter)} without an exception is not allowed when this pipe writer has unflushed bytes.");
            }

            if (exception is null)
            {
                _stream.CompleteWrites();
            }
            else
            {
                // We don't use the application error code, it's irrelevant.
                _stream.CompleteWrites(errorCode: 0ul);
            }

            _pipe.Writer.Complete();
            if (_state.TrySetFlag(State.PipeReaderCompleted))
            {
                _pipe.Reader.Complete();
            }

            _sendCreditSemaphore.Dispose();
            _abortCts.Dispose();
        }
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) =>
        // WriteAsync will flush the internal buffer
        WriteAsync(ReadOnlySequence<byte>.Empty, endStream: false, cancellationToken);

    public override Memory<byte> GetMemory(int sizeHint) => _pipe.Writer.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint) => _pipe.Writer.GetSpan(sizeHint);

    public override ValueTask<FlushResult> WriteAsync(
        ReadOnlyMemory<byte> source,
        CancellationToken cancellationToken) =>
        WriteAsync(new ReadOnlySequence<byte>(source), endStream: false, cancellationToken);

    public override async ValueTask<FlushResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken)
    {
        CheckIfCompleted();

        // Abort the stream if the invocation is canceled.
        using CancellationTokenRegistration cancelTokenRegistration = cancellationToken.UnsafeRegister(
                cts => ((CancellationTokenSource)cts!).Cancel(),
                _abortCts);

        ReadResult readResult = default;
        try
        {
            if (!_state.TrySetFlag(State.PipeReaderInUse))
            {
                throw new InvalidOperationException($"The {nameof(WriteAsync)} operation is not thread safe");
            }

            if (_state.HasFlag(State.PipeReaderCompleted))
            {
                return _exception is null ?
                    new FlushResult(isCanceled: false, isCompleted: true) :
                    throw ExceptionUtil.Throw(_exception);
            }

            if (_pipe.Writer.UnflushedBytes > 0)
            {
                // Flush the internal pipe. It can be completed if the peer sent the stop sending frame.
                FlushResult flushResult = await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                Debug.Assert(!flushResult.IsCanceled); // CancelPendingFlush is never called on _pipe.Writer
                if (flushResult.IsCompleted)
                {
                    return new FlushResult(isCanceled: false, isCompleted: true);
                }
            }

            ReadOnlySequence<byte> source1;
            ReadOnlySequence<byte> source2;
            if (_pipe.Reader.TryRead(out readResult))
            {
                Debug.Assert(!readResult.IsCanceled && !readResult.IsCompleted && readResult.Buffer.Length > 0);
                source1 = readResult.Buffer;
                source2 = source;
            }
            else
            {
                source1 = source;
                source2 = ReadOnlySequence<byte>.Empty;
            }

            if (source1.IsEmpty && source2.IsEmpty && !endStream)
            {
                // WriteAsync is called with an empty buffer, typically by a call to FlushAsync. Some payload writers
                // such as the deflate compressor might do this.
                return new FlushResult(isCanceled: false, isCompleted: false);
            }

            return await _stream.SendStreamFrameAsync(
                source1,
                source2,
                endStream,
                _abortCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Debug.Assert(_abortCts.IsCancellationRequested);
            return _exception is null ?
                new FlushResult(isCanceled: false, isCompleted: true) :
                throw ExceptionUtil.Throw(_exception);
        }
        finally
        {
            if (readResult.Buffer.Length > 0)
            {
                _pipe.Reader.AdvanceTo(readResult.Buffer.End);

                // Make sure there's no more data to consume from the pipe.
                Debug.Assert(!_pipe.Reader.TryRead(out ReadResult _));
            }

            if (_state.HasFlag(State.PipeReaderCompleted))
            {
                // If the pipe reader has been completed while we were writing the stream data, we make sure to
                // complete the reader now since Complete or Abort didn't do it.
                _pipe.Reader.Complete(_exception);
            }
            _state.ClearFlag(State.PipeReaderInUse);
        }
    }

    internal SlicPipeWriter(SlicStream stream, SlicConnection connection)
    {
        _stream = stream;
        _sendCredit = connection.PeerPauseWriterThreshold;

        // Create a pipe that never pauses on flush or write. The SlicePipeWriter will pause the flush or write if
        // the Slic flow control doesn't permit sending more data. We also use an inline pipe scheduler for write to
        // avoid thread context switches when FlushAsync is called on the internal pipe writer.
        _pipe = new(new PipeOptions(
            pool: connection.Pool,
            minimumSegmentSize: connection.MinSegmentSize,
            pauseWriterThreshold: 0,
            writerScheduler: PipeScheduler.Inline));
    }

    /// <summary>Aborts writes.</summary>
    /// <param name="exception">The exception raised by <see cref="PipeWriter.WriteAsync" /> or <see cref="FlushAsync"
    /// />.</param>
    internal void Abort(IceRpcException? exception)
    {
        Interlocked.CompareExchange(ref _exception, exception, null);

        // Don't complete the reader if it's being used concurrently for sending a frame. It will be completed
        // once the reading terminates.
        if (_state.TrySetFlag(State.PipeReaderCompleted))
        {
            // Cancel write if pending.
            _abortCts.Cancel();

            if (!_state.HasFlag(State.PipeReaderInUse))
            {
                _pipe.Reader.Complete(exception);
            }
        }
    }

    internal async ValueTask<int> AcquireSendCreditAsync(CancellationToken cancellationToken)
    {
        // Acquire the semaphore to ensure flow control allows sending additional data. It's important to acquire the
        // semaphore before checking _sendCredit. The semaphore acquisition will block if we can't send additional data
        // (_sendCredit == 0). Acquiring the semaphore ensures that we are allowed to send additional data and
        // _sendCredit can be used to figure out the size of the next packet to send.
        await _sendCreditSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return _sendCredit;
    }

    internal void ConsumedSendCredit(int consumed)
    {
        // Decrease the size of remaining data that we are allowed to send. If all the credit is consumed, _sendCredit
        // will be 0 and we don't release the semaphore to prevent further sends. The semaphore will be released once
        // the stream receives a StreamConsumed frame.
        int sendCredit = Interlocked.Add(ref _sendCredit, -consumed);
        if (sendCredit > 0)
        {
            _sendCreditSemaphore.Release();
        }
        Debug.Assert(sendCredit >= 0);
    }

    internal int ReceivedConsumedFrame(int size)
    {
        int newValue = Interlocked.Add(ref _sendCredit, size);
        if (newValue == size)
        {
            try
            {
                Debug.Assert(_sendCreditSemaphore.CurrentCount == 0);
                _sendCreditSemaphore.Release();
            }
            catch (ObjectDisposedException)
            {
                // Expected if the writer has been completed.
                Debug.Assert(_state.HasFlag(State.Completed));
            }
        }
        return newValue;
    }

    private void CheckIfCompleted()
    {
        if (_state.HasFlag(State.Completed))
        {
            // If the writer is completed, the caller is bogus, it shouldn't call write operations after completing the
            // pipe writer.
            throw new InvalidOperationException("Writing is not allowed once the writer is completed.");
        }
    }

    /// <summary>The state enumeration is used to ensure the writer is not used after it's completed and to ensure
    /// that the internal pipe reader isn't completed concurrently when it's being used by WriteAsync.</summary>
    private enum State : int
    {
        /// <summary><see cref="Complete" /> was called on this Slic pipe writer.</summary>
        Completed = 1,

        /// <summary>Data is being read from the internal pipe reader.</summary>
        PipeReaderInUse = 2,

        /// <summary>The internal pipe reader was completed by <see cref="Abort" />.</summary>
        PipeReaderCompleted = 4
    }
}
