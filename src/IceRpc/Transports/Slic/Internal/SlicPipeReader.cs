// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Slic.Internal;

// The SlicPipeReader doesn't override ReadAtLeastAsyncCore or CopyToAsync methods because:
// - we can't forward the calls to the internal pipe reader since reading relies on the AdvanceTo implementation to send
//   the StreamConsumed frame once the data is examined,
// - the default implementation can't be much optimized.
internal class SlicPipeReader : PipeReader
{
    private int _examined;
    private volatile Exception? _exception;
    private long _lastExaminedOffset;
    private readonly Pipe _pipe;
    private ReadResult _readResult;
    private int _receiveCredit;
    private readonly int _resumeThreshold;
    // FlagEnumExtensions operations are used to update the state. These operations are atomic and don't require mutex
    // locking.
    private int _state;
    private readonly SlicStream _stream;

    public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        ThrowIfCompleted();

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
            _stream.WriteStreamConsumedFrame(_examined);
            _examined = 0;
        }

        _pipe.Reader.AdvanceTo(consumed, examined);
    }

    public override void CancelPendingRead() => _pipe.Reader.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        if (_state.TrySetFlag(State.Completed))
        {
            // Forcefully close the stream reads if reads were not already gracefully closed by ReadAsync or TryRead.
            _stream.CloseReads(graceful: false);

            CompleteReads(exception: null);

            _pipe.Reader.Complete();
        }
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfCompleted();

        if (_exception is not null)
        {
            _stream.ThrowIfConnectionClosed();
        }

        ReadResult result = await _pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsCanceled)
        {
            return GetReadResult(result);
        }

        // Cache the read result for the implementation of AdvanceTo that needs to figure out how much data got examined
        // and consumed.
        _readResult = result;

        // All the data from the peer is considered read at this point. It's time to close reads on the stream. This
        // will write the StreamReadsClosed frame to the peer and allow it to release the stream semaphore.
        if (result.IsCompleted)
        {
            _stream.CloseReads(graceful: true);
        }

        return result;
    }

    public override bool TryRead(out ReadResult result)
    {
        ThrowIfCompleted();

        if (_exception is not null)
        {
            _stream.ThrowIfConnectionClosed();
        }

        if (_pipe.Reader.TryRead(out result))
        {
            if (result.IsCanceled)
            {
                result = GetReadResult(result);
                return true;
            }

            // Cache the read result for the implementation of AdvanceTo that needs to figure out how much data got
            // examined and consumed.
            _readResult = result;

            // All the data from the peer is considered read at this point. It's time to close reads on the stream. This
            // will write the StreamReadsClosed frame to the peer and allow it to release the stream semaphore.
            if (result.IsCompleted)
            {
                _stream.CloseReads(graceful: true);
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

        // We keep the default readerScheduler (ThreadPool) because the _pipe.Writer.FlushAsync executes in the
        // "read loop task" and we don't want this task to continue into application code. The writerScheduler
        // doesn't matter since _pipe.Writer.FlushAsync never blocks.
        _pipe = new(new PipeOptions(
            pool: connection.Pool,
            pauseWriterThreshold: 0,
            minimumSegmentSize: connection.MinSegmentSize,
            useSynchronizationContext: false));
    }

    /// <summary>Completes reads.</summary>
    /// <param name="exception">The exception that will be raised by <see cref="ReadAsync" /> or <see cref="TryRead" />
    /// operation.</param>
    internal void CompleteReads(Exception? exception)
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

    /// <summary>Notifies the reader of the reception of a <see cref="FrameType.Stream" /> or <see
    /// cref="FrameType.StreamLast" /> frame. The stream data is consumed from the connection and buffered by this
    /// reader on its internal pipe.</summary>
    /// <returns><see langword="true" /> if the data was consumed; otherwise, <see langword="false"/> if the reader was
    /// completed by the application.</returns>
    internal async ValueTask<bool> ReceivedStreamFrameAsync(
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
                return false; // No bytes consumed because the application completed the stream input.
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
                _pipe.Writer.Complete();
            }
            else
            {
                _ = await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }

            return true;
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

    private ReadResult GetReadResult(ReadResult readResult)
    {
        // This method is called by ReadAsync or TryRead when the read operation on _pipe.Reader returns a canceled read
        // result (IsCanceled=true). The _pipe.Reader ReadAsync/TryRead operations can return a canceled read result for
        // two different reasons:
        // - the application called CancelPendingRead
        // - the connection is closed while data is written on _pipe.Writer
        Debug.Assert(readResult.IsCanceled);

        if (_state.HasFlag(State.PipeWriterCompleted))
        {
            // The connection was closed while the pipe writer was in use. Either throw or return a non-canceled result
            // depending on the completion exception.
            if (_exception is null)
            {
                return new ReadResult(readResult.Buffer, isCanceled: false, isCompleted: true);
            }
            else
            {
                throw ExceptionUtil.Throw(_exception);
            }
        }
        else
        {
            // The application called CancelPendingRead, return the read result as-is.
            return readResult;
        }
    }

    private void ThrowIfCompleted()
    {
        if (_state.HasFlag(State.Completed))
        {
            // If the reader is completed, the caller is bogus, it shouldn't call read operations after completing the
            // pipe reader.
            throw new InvalidOperationException("Reading is not allowed once the reader is completed.");
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

        /// <summary>The internal pipe writer was completed by <see cref="CompleteReads" />.</summary>
        PipeWriterCompleted = 4,
    }
}
