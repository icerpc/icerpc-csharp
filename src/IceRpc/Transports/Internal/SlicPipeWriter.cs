// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;
using System.Diagnostics;

namespace IceRpc.Transports.Internal
{
    internal class SlicPipeWriter : ReadOnlySequencePipeWriter
    {
        private bool _isCompleted;
        private readonly Pipe _pipe;
        private readonly SlicMultiplexedStream _stream;

        public override void Advance(int bytes)
        {
            CheckIfCompleted();
            _pipe.Writer.Advance(bytes);
        }

        public override void CancelPendingFlush() => _pipe.Writer.CancelPendingFlush();

        public override void Complete(Exception? exception = null)
        {
            if (!_isCompleted)
            {
                // If writes aren't marked as completed yet, abort stream writes. This will send a stream reset frame to
                // the peer to notify it won't receive additional data.
                if (!_stream.WritesCompleted)
                {
                    if (exception == null)
                    {
                        if (_pipe.Writer.UnflushedBytes > 0)
                        {
                            throw new NotSupportedException(
                                $"can't complete {nameof(SlicPipeWriter)} with unflushed bytes");
                        }
                        _stream.AbortWrite(SlicStreamError.NoError.ToError());
                    }
                    else if (exception is MultiplexedStreamAbortedException abortedException)
                    {
                        _stream.AbortWrite(abortedException.ToError());
                    }
                    else
                    {
                        _stream.AbortWrite(SlicStreamError.UnexpectedError.ToError());
                    }
                }

                _pipe.Writer.Complete(exception);
                _pipe.Reader.Complete(exception);

                // Mark the writer as completed after calling WriteAsync, it would throw otherwise.
                _isCompleted = true;
            }
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancel) =>
            // WriteAsync will flush the internal buffer
            WriteAsync(ReadOnlySequence<byte>.Empty, completeWhenDone: false, cancel);

        public override ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancel) =>
            // Writing an empty buffer completes the stream.
            WriteAsync(new ReadOnlySequence<byte>(source), completeWhenDone: source.Length == 0, cancel);

        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool completeWhenDone,
            CancellationToken cancel)
        {
            CheckIfCompleted();

            if (_stream.WritesCompleted)
            {
                if (_stream.ResetError is long error &&
                    error.ToSlicError() is SlicStreamError slicError &&
                    slicError != SlicStreamError.NoError)
                {
                    throw new MultiplexedStreamAbortedException(error);
                }
                else
                {
                    return new FlushResult(isCanceled: false, isCompleted: true);
                }
            }

            if (_pipe.Writer.UnflushedBytes > 0)
            {
                // Flush the internal pipe.
                _ = await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);

                // Read the data from the pipe.
                ReadResult readResult = await _pipe.Reader.ReadAsync(CancellationToken.None).ConfigureAwait(false);
                Debug.Assert(!readResult.IsCanceled);
                if (readResult.IsCompleted)
                {
                    // The peer sent the stop sending frame.
                    _pipe.Reader.AdvanceTo(readResult.Buffer.End);
                    return new FlushResult(isCanceled: false, isCompleted: true);
                }

                Debug.Assert(readResult.Buffer.Length > 0);
                try
                {
                    // Send the unflushed bytes and the source.
                    return await _stream.SendStreamFrameAsync(
                        readResult.Buffer,
                        source,
                        completeWhenDone,
                        cancel).ConfigureAwait(false);
                }
                finally
                {
                    _pipe.Reader.AdvanceTo(readResult.Buffer.End);

                    // Make sure there's no more data to consume from the pipe.
                    Debug.Assert(!_pipe.Reader.TryRead(out ReadResult _));
                }
            }
            else if (source.Length > 0)
            {
                // If there's no unflushed bytes, we just send the source.
                return await _stream.SendStreamFrameAsync(
                    source,
                    ReadOnlySequence<byte>.Empty,
                    completeWhenDone,
                    cancel).ConfigureAwait(false);
            }
            else
            {
                return new FlushResult(isCanceled: false, isCompleted: false);
            }
        }

        public override Memory<byte> GetMemory(int sizeHint) => _pipe.Writer.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint) => _pipe.Writer.GetSpan(sizeHint);

        internal void ReceivedStopSendingFrame(long error)
        {
            // TODO: Look into cancelling the _stream.SendStreamFrameAsync() call if it's pending?
            if (error.ToSlicError() == SlicStreamError.NoError)
            {
                _pipe.Writer.Complete();
            }
            else
            {
                _pipe.Writer.Complete(new MultiplexedStreamAbortedException(error));
            }
        }

        internal SlicPipeWriter(SlicMultiplexedStream stream, MemoryPool<byte> pool, int minimumSegmentSize)
        {
            _stream = stream;

            // Create a pipe that never pauses on flush or write. The SlicePipeWriter will pause the flush or write if
            // the Slic flow control doesn't permit sending more data. We also use an inline pipe scheduler for write to
            // avoid thread context switches when FlushAsync is called on the internal pipe writer.
            _pipe = new(new PipeOptions(
                pool: pool,
                minimumSegmentSize: minimumSegmentSize,
                pauseWriterThreshold: 0,
                writerScheduler: PipeScheduler.Inline));
        }

        private void CheckIfCompleted()
        {
            if (_isCompleted)
            {
                // If the writer is completed, the caller is bogus, it shouldn't call writer operations after completing
                // the pipe writer.
                throw new InvalidOperationException("writing is not allowed once the writer is completed");
            }
        }
    }
}
