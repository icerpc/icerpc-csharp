// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Slic.Internal;

#pragma warning disable CA1001 // Type owns disposable field(s) '_completeWritesCts' but is not disposable
internal class SlicPipeWriter : ReadOnlySequencePipeWriter
#pragma warning restore CA1001
{
    // We can avoid disposing _completeWritesCts because it was not created using CreateLinkedTokenSource, and it
    // doesn't use a timer. It is not easy to dispose it because CompleteWrites can be called by another thread after
    // Complete has been called.
    private readonly CancellationTokenSource _completeWritesCts = new();
    private Exception? _exception;
    private bool _isCompleted;
    private readonly Pipe _pipe;
    private volatile int _sendCredit = int.MaxValue;
    // The semaphore is used when flow control is enabled to wait for additional send credit to be available.
    private readonly SemaphoreSlim _sendCreditSemaphore = new(1, 1);
    private readonly SlicStream _stream;

    public override void Advance(int bytes)
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException("Writing is not allowed once the writer is completed.");
        }
        _pipe.Writer.Advance(bytes);
    }

    // SlicPipeWriter does not support this method: the IceRPC core does not need it. And when the application code
    // installs a payload writer interceptor, this interceptor should never call it on "next".
    public override void CancelPendingFlush() => throw new NotSupportedException();

    public override void Complete(Exception? exception = null)
    {
        if (!_isCompleted)
        {
            _isCompleted = true;

            if (exception is null && _pipe.Writer.UnflushedBytes > 0)
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
            _pipe.Reader.Complete();
            _sendCreditSemaphore.Dispose();
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
        if (_isCompleted)
        {
            throw new InvalidOperationException("Writing is not allowed once the writer is completed.");
        }

        // Flush the pipe before the check for the close connection. This makes sure that the check for unflushed data
        // on successful compete succeeds. See the Complete implementation above.
        if (_pipe.Writer.UnflushedBytes > 0)
        {
            await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _stream.ThrowIfConnectionClosed();

        // Abort the stream if the invocation is canceled.
        using CancellationTokenRegistration cancelTokenRegistration = cancellationToken.UnsafeRegister(
            cts => ((CancellationTokenSource)cts!).Cancel(),
            _completeWritesCts);

        ReadOnlySequence<byte> source1;
        ReadOnlySequence<byte> source2;
        if (_pipe.Reader.TryRead(out ReadResult readResult))
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
            // WriteAsync is called with an empty buffer, typically by a call to FlushAsync. Some payload writers such
            // as the deflate compressor might do this.
            return new FlushResult(isCanceled: false, isCompleted: false);
        }

        try
        {
            return await _stream.WriteStreamFrameAsync(
                source1,
                source2,
                endStream,
                _completeWritesCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        }
    }

    internal SlicPipeWriter(SlicStream stream, SlicConnection connection)
    {
        _stream = stream;
        _sendCredit = connection.PeerPauseWriterThreshold;

        // Create a pipe that never pauses on flush or write. The SlicePipeWriter will pause the flush or write if
        // the Slic flow control doesn't permit sending more data.
        // The readerScheduler doesn't matter (we don't call _pipe.Reader.ReadAsync) and the writerScheduler doesn't
        // matter (_pipe.Writer.FlushAsync never blocks).
        _pipe = new(new PipeOptions(
            pool: connection.Pool,
            minimumSegmentSize: connection.MinSegmentSize,
            pauseWriterThreshold: 0,
            useSynchronizationContext: false));
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

    /// <summary>Complete writes.</summary>
    /// <param name="exception">The exception raised by <see cref="PipeWriter.WriteAsync" /> or <see cref="FlushAsync"
    /// />.</param>
    internal void CompleteWrites(Exception? exception)
    {
        Interlocked.CompareExchange(ref _exception, exception, null);
        _completeWritesCts.Cancel();
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
                Debug.Assert(_isCompleted);
            }
        }
        return newValue;
    }
}
