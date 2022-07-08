// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

internal class SlicPipeWriter : ReadOnlySequencePipeWriter
{
    private Exception? _exception;
    private readonly Pipe _pipe;
    private readonly IMultiplexedStreamErrorCodeConverter _errorCodeConverter;
    private int _state;
    private readonly SlicMultiplexedStream _stream;

    public override void Advance(int bytes)
    {
        CheckIfCompleted();
        _pipe.Writer.Advance(bytes);
    }

    public override void CancelPendingFlush() => _pipe.Writer.CancelPendingFlush();

    public override void Complete(Exception? exception = null)
    {
        if (_state.TrySetFlag(State.Completed))
        {
            // If writes aren't marked as completed yet, abort stream writes. This will send a stream reset frame to
            // the peer to notify it won't receive additional data.
            if (!_stream.WritesCompleted)
            {
                if (exception is null && _pipe.Writer.UnflushedBytes > 0)
                {
                    throw new NotSupportedException(
                        $"can't complete {nameof(SlicPipeWriter)} with unflushed bytes");
                }

                _stream.AbortWrite(_errorCodeConverter.ToErrorCode(exception));
            }

            _pipe.Writer.Complete(exception);
            CompletePipeReader(exception);
        }
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancel) =>
        // WriteAsync will flush the internal buffer
        WriteAsync(ReadOnlySequence<byte>.Empty, endStream: false, cancel);

    public override Memory<byte> GetMemory(int sizeHint) => _pipe.Writer.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint) => _pipe.Writer.GetSpan(sizeHint);

    public override ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancel) =>
        // Writing an empty buffer completes the stream.
        WriteAsync(new ReadOnlySequence<byte>(source), endStream: source.Length == 0, cancel);

    public override async ValueTask<FlushResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancel)
    {
        CheckIfCompleted();

        if (_state.HasFlag(State.PipeReaderCompleted))
        {
            return GetFlushResult();
        }

        if (_pipe.Writer.UnflushedBytes > 0)
        {
            if (!_state.TrySetFlag(State.PipeReaderInUse))
            {
                throw new InvalidOperationException($"{nameof(WriteAsync)} is not thread safe");
            }

            try
            {
                // Flush the internal pipe. It can be completed if the peer sent the stop sending frame.
                FlushResult flushResult = await _pipe.Writer.FlushAsync(
                    CancellationToken.None).ConfigureAwait(false);
                if (flushResult.IsCanceled)
                {
                    return GetFlushResult();
                }
                else if (flushResult.IsCompleted)
                {
                    return flushResult;
                }

                // Read the data from the pipe.
                ReadResult readResult = await _pipe.Reader.ReadAsync(CancellationToken.None).ConfigureAwait(false);

                Debug.Assert(!readResult.IsCanceled && !readResult.IsCompleted && readResult.Buffer.Length > 0);
                try
                {
                    // Send the unflushed bytes and the source.
                    return await _stream.SendStreamFrameAsync(
                        readResult.Buffer,
                        source,
                        endStream,
                        cancel).ConfigureAwait(false);
                }
                finally
                {
                    _pipe.Reader.AdvanceTo(readResult.Buffer.End);

                    // Make sure there's no more data to consume from the pipe.
                    Debug.Assert(!_pipe.Reader.TryRead(out ReadResult _));
                }
            }
            finally
            {
                if (_state.HasFlag(State.PipeReaderCompleted))
                {
                    // If the pipe reader has been completed while we were writing the stream data, we make sure to
                    // complete the reader now since Complete or CompleteReader didn't do it.
                    await _pipe.Reader.CompleteAsync(_exception).ConfigureAwait(false);
                }
                _state.ClearFlag(State.PipeReaderInUse);
            }
        }
        else if (source.Length > 0 || endStream)
        {
            // If there's no unflushed bytes, we just send the source.
            return await _stream.SendStreamFrameAsync(
                source,
                ReadOnlySequence<byte>.Empty,
                endStream,
                cancel).ConfigureAwait(false);
        }
        else
        {
            // WriteAsync is called with an empty buffer and completeWhenDone = false. Some payload writers such as
            // the deflate compressor might do this.
            return new FlushResult(isCanceled: false, isCompleted: false);
        }

        FlushResult GetFlushResult()
        {
            if (_state.HasFlag(State.PipeReaderCompleted))
            {
                if (_exception is not null)
                {
                    throw ExceptionUtil.Throw(_exception);
                }
                return new FlushResult(isCanceled: false, isCompleted: true);
            }
            else
            {
                return new FlushResult(isCanceled: true, isCompleted: false);
            }
        }
    }

    internal SlicPipeWriter(
        SlicMultiplexedStream stream,
        IMultiplexedStreamErrorCodeConverter errorCodeConverter,
        MemoryPool<byte> pool,
        int minimumSegmentSize)
    {
        _stream = stream;
        _errorCodeConverter = errorCodeConverter;

        // Create a pipe that never pauses on flush or write. The SlicePipeWriter will pause the flush or write if
        // the Slic flow control doesn't permit sending more data. We also use an inline pipe scheduler for write to
        // avoid thread context switches when FlushAsync is called on the internal pipe writer.
        _pipe = new(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: 0,
            writerScheduler: PipeScheduler.Inline));
    }

    internal void ReceivedStopSendingFrame(ulong errorCode) =>
        CompletePipeReader(_errorCodeConverter.FromErrorCode(errorCode));

    internal void Shutdown(Exception exception) => CompletePipeReader(exception);

    private void CheckIfCompleted()
    {
        if (_state.HasFlag(State.Completed))
        {
            // If the writer is completed, the caller is bogus, it shouldn't call writer operations after completing
            // the pipe writer.
            throw new InvalidOperationException("writing is not allowed once the writer is completed");
        }
    }

    private void CompletePipeReader(Exception? exception)
    {
        _exception ??= exception;

        // Don't complete the reader if it's being used concurrently for sending a frame. It will be completed
        // once the reading terminates.
        if (_state.TrySetFlag(State.PipeReaderCompleted))
        {
            if (_state.HasFlag(State.PipeReaderInUse))
            {
                _pipe.Writer.CancelPendingFlush();
            }
            else
            {
                _pipe.Reader.Complete(_exception);
            }
        }
    }

    /// <summary>The state enumeration is used to ensure the writer is not used after it's completed and to ensure
    /// that the internal pipe reader isn't completed concurrently when it's being used by WriteAsync.</summary>
    private enum State : int
    {
        /// <summary><see cref="Complete"/> was called on this Slic pipe writer.</summary>
        Completed = 1,

        /// <summary>Data is being read from the internal pipe reader.</summary>
        PipeReaderInUse = 2,

        /// <summary>The internal pipe reader was completed either by <see cref="Complete"/> or <see
        /// cref="ReceivedStopSendingFrame"/>.</summary>
        PipeReaderCompleted = 4
    }
}
