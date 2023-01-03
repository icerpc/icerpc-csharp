// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

internal class SlicPipeReader : PipeReader
{
    private bool _completedReads;
    private int _examined;
    private IceRpcException? _exception;
    private long _lastExaminedOffset;
    private readonly Pipe _pipe;
    private ReadResult _readResult;
    private int _receiveCredit;
    private readonly int _resumeThreshold;
    private int _state;
    private readonly SlicStream _stream;

    public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        CheckIfCompleted();

        long startOffset = _readResult.Buffer.GetOffset(_readResult.Buffer.Start);
        long consumedOffset = _readResult.Buffer.GetOffset(consumed) - startOffset;
        long examinedOffset = _readResult.Buffer.GetOffset(examined) - startOffset;

        // Add the additional examined bytes to the examined bytes total.
        _examined += (int)(examinedOffset - _lastExaminedOffset);
        _lastExaminedOffset = examinedOffset - consumedOffset;

        // If the number of examined bytes is superior to the resume threshold notifies the sender it's safe to send
        // additional data.
        if (_examined >= _resumeThreshold)
        {
            Interlocked.Add(ref _receiveCredit, _examined);
            _stream.SendStreamConsumed(_examined);
            _examined = 0;
        }

        _pipe.Reader.AdvanceTo(consumed, examined);
    }

    public override void CancelPendingRead() => _pipe.Reader.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        if (_state.TrySetFlag(State.Completed))
        {
            // If ReadAsync or TryRead didn't complete reads on the stream already, complete them now. This will
            // send a stop sending frame if reads are not completed already.
            if (!_completedReads)
            {
                // If the peer is not done writing, send a stop sending frame.
                _stream.CompleteReads(errorCode: 0ul);
            }

            if (_state.TrySetFlag(State.PipeWriterCompleted))
            {
                _pipe.Writer.Complete();
            }
            _pipe.Reader.Complete();
        }
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        CheckIfCompleted();

        if (_state.HasFlag(State.PipeWriterCompleted))
        {
            return GetReadResult();
        }

        ReadResult result = await _pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsCanceled)
        {
            return GetReadResult();
        }

        // Cache the read result for the implementation of AdvanceTo that needs to figure out how much data got examined
        // and consumed.
        _readResult = result;

        // All the data from the peer is considered read at this point. It's time to complete reads on the stream. This
        // will send the StreamReadsCompleted to the peer and allow it to release the stream semaphore.
        if (result.IsCompleted && !_completedReads)
        {
            _completedReads = true;
            _stream.CompleteReads();
        }

        return result;
    }

    public override bool TryRead(out ReadResult result)
    {
        CheckIfCompleted();

        if (_state.HasFlag(State.PipeWriterCompleted))
        {
            result = GetReadResult();
            return true;
        }

        if (_pipe.Reader.TryRead(out result))
        {
            if (result.IsCanceled)
            {
                result = GetReadResult();
                return true;
            }

            // Cache the read result for the implementation of AdvanceTo that needs to figure out how much data got
            // examined and consumed.
            _readResult = result;

            // All the data from the peer is considered read at this point. It's time to complete reads on the stream.
            // This will send the StreamReadsCompleted to the peer and allow it to release the stream semaphore.
            if (result.IsCompleted && !_completedReads)
            {
                _completedReads = true;
                _stream.CompleteReads();
            }
            return true;
        }
        else
        {
            return false;
        }
    }

    internal SlicPipeReader(SlicStream stream, SlicConnection connection)
    {
        _stream = stream;
        _resumeThreshold = connection.ResumeWriterThreshold;
        _receiveCredit = connection.PauseWriterThreshold;
        _pipe = new(new PipeOptions(
            pool: connection.Pool,
            pauseWriterThreshold: 0,
            minimumSegmentSize: connection.MinSegmentSize,
            writerScheduler: PipeScheduler.Inline));
    }

    /// <summary>Aborts reads.</summary>
    /// <param name="exception">The exception raised by ReadAsync.</param>
    internal void Abort(IceRpcException? exception)
    {
        Interlocked.CompareExchange(ref _exception, exception, null);

        if (_state.TrySetFlag(State.PipeWriterCompleted))
        {
            if (_state.HasFlag(State.PipeWriterInUse))
            {
                _pipe.Reader.CancelPendingRead();
            }
            else
            {
                _pipe.Writer.Complete(exception);
            }
        }
    }

    /// <summary>Called when a stream frame is received. It writes the data from the received stream frame to the
    /// internal pipe writer and returns the number of bytes that were consumed.</summary>
    internal async ValueTask<int> ReceivedStreamFrameAsync(
        int dataSize,
        bool endStream,
        CancellationToken cancellationToken)
    {
        if (dataSize == 0 && !endStream)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "An empty Slic stream frame is not allowed unless endStream is true.");
        }

        if (!_state.TrySetFlag(State.PipeWriterInUse))
        {
            throw new InvalidOperationException(
                $"The {nameof(ReceivedStreamFrameAsync)} operation is not thread safe.");
        }

        try
        {
            if (_state.HasFlag(State.PipeWriterCompleted))
            {
                // If the Slic pipe reader is completed, nothing was consumed.
                return 0;
            }

            int newCredit = Interlocked.Add(ref _receiveCredit, -dataSize);
            if (newCredit < 0)
            {
                throw new IceRpcException(
                    IceRpcError.IceRpcError,
                    "Received more data than flow control permits.");
            }

            // Fill the pipe writer with dataSize bytes.
            await _stream.FillBufferWriterAsync(
                    _pipe.Writer,
                    dataSize,
                    cancellationToken).ConfigureAwait(false);

            if (endStream)
            {
                // If it's not a remote stream and the peer is done sending data, we can complete reads right away to
                // allow a new stream to be opened. There's no need to wait for the buffered data or end of stream to be
                // consumed. This will prevent the sending of a stop sending frame when this reader is completed.
                if (!_stream.IsRemote && dataSize == 0)
                {
                    _stream.TrySetReadsCompleted();
                }
                _pipe.Writer.Complete();
            }
            else
            {
                _ = await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }

            return dataSize;
        }
        finally
        {
            if (_state.HasFlag(State.PipeWriterCompleted))
            {
                // If the pipe writer has been completed while we were reading the data from the stream, we make sure to
                // complete the writer now since Complete or CompleteWriter didn't do it.
                _pipe.Writer.Complete(_exception);
            }
            _state.ClearFlag(State.PipeWriterInUse);
        }
    }

    private void CheckIfCompleted()
    {
        if (_state.HasFlag(State.Completed))
        {
            // If the reader is completed, the caller is bogus, it shouldn't call read operations after completing the
            // pipe reader.
            throw new InvalidOperationException("Reading is not allowed once the reader is completed.");
        }
    }

    private ReadResult GetReadResult()
    {
        if (_state.HasFlag(State.PipeWriterCompleted))
        {
            if (_exception is null)
            {
                return new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true);
            }
            else
            {
                throw ExceptionUtil.Throw(_exception);
            }
        }
        else
        {
            return new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled: true, isCompleted: false);
        }
    }

    /// <summary>The state enumeration is used to ensure the reader is not used after it's completed and to ensure that
    /// the internal pipe writer isn't completed concurrently when it's being used by <see
    /// cref="ReceivedStreamFrameAsync" />.</summary>
    private enum State : int
    {
        /// <summary><see cref="Complete" /> was called on this Slic pipe reader.</summary>
        Completed = 1,

        /// <summary>Data is being written to the internal pipe writer.</summary>
        PipeWriterInUse = 2,

        /// <summary>The internal pipe writer was completed by <see cref="Abort" />.</summary>
        PipeWriterCompleted = 4,
    }
}
