// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    internal class SlicPipeReader : PipeReader
    {
        private int _additionalCredit;
        private Exception? _exception;
        private long _lastExaminedOffset;
        private readonly Pipe _pipe;
        private readonly Func<Memory<byte>, CancellationToken, ValueTask> _readUntilFullFunc;
        private ReadResult _readResult;
        private int _receiveCredit;
        private readonly int _resumeThreshold;
        private int _state;
        private readonly SlicMultiplexedStream _stream;

        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            CheckIfCompleted();

            long startOffset = _readResult.Buffer.GetOffset(_readResult.Buffer.Start);
            long consumedOffset = _readResult.Buffer.GetOffset(consumed) - startOffset;
            long examinedOffset = _readResult.Buffer.GetOffset(examined) - startOffset;

            // Add the additional examined bytes to the additional receive credit.
            _additionalCredit += (int)(examinedOffset - _lastExaminedOffset);
            _lastExaminedOffset = examinedOffset - consumedOffset;

            // If the additional received credit is superior to the resume threshold notifies the sender that it can
            // send additional data.
            if (_additionalCredit >= _resumeThreshold)
            {
                Interlocked.Add(ref _receiveCredit, (int)_additionalCredit);
                _stream.SendStreamConsumed(_additionalCredit);
                _additionalCredit = 0;
            }

            // If we reached the end of the sequence and the peer won't be sending additional data, we can mark reads as
            // completed on the stream.
            bool isRemoteWriteCompleted =
                _readResult.IsCompleted &&
                consumedOffset == _readResult.Buffer.GetOffset(_readResult.Buffer.End) - startOffset;

            _pipe.Reader.AdvanceTo(consumed, examined);

            if (isRemoteWriteCompleted)
            {
                // The application consumed all the byes and the peer is done sending data, we can mark reads as
                // completed on the stream.
                _stream.TrySetReadCompleted();
            }
        }

        public override void CancelPendingRead() => _pipe.Reader.CancelPendingRead();

        public override void Complete(Exception? exception = null)
        {
            if (_state.TrySetFlag(State.Completed))
            {
                if (_readResult.IsCompleted)
                {
                    // If the peer is no longer sending data, just mark reads as completed on the stream.
                    _stream.TrySetReadCompleted();
                }

                if (!_stream.ReadsCompleted)
                {
                    // If reads aren't marked as completed yet, abort stream reads. This will send a stream stop sending
                    // frame to the peer to notify it shouldn't send additional data.
                    if (exception == null)
                    {
                        _stream.AbortRead(SlicStreamError.NoError.ToError());
                    }
                    else if (exception is MultiplexedStreamAbortedException abortedException)
                    {
                        _stream.AbortRead(abortedException.ToError());
                    }
                    else
                    {
                        _stream.AbortRead(SlicStreamError.UnexpectedError.ToError());
                    }
                }

                _pipe.Reader.Complete(exception);

                // Don't complete the writer if it's being used concurrently for receiving a frame. It will be completed
                // once the reading terminates.
                if (_state.TrySetFlag(State.PipeWriterCompleted))
                {
                    if (_state.HasFlag(State.PipeWriterInUse))
                    {
                        _exception = exception;
                    }
                    else
                    {
                        _pipe.Writer.Complete(exception);
                    }
                }
            }
        }

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancel = default)
        {
            CheckIfCompleted();

            ReadResult result = await _pipe.Reader.ReadAsync(cancel).ConfigureAwait(false);

            // Cache the read result for the implementation of AdvanceTo. It needs to be able to figure out how much
            // data got examined and consumed. It also needs to know if the reader is completed to mark reads as
            // completed on the stream.
            _readResult = result;
            if (result.Buffer.IsEmpty && result.IsCompleted)
            {
                // Nothing to read and the writer is done, we can mark stream reads as completed now to release the
                // stream count.
                _stream.TrySetReadCompleted();
            }
            return result;
        }

        public override bool TryRead(out ReadResult result)
        {
            CheckIfCompleted();

            if (_pipe.Reader.TryRead(out result))
            {
                // Cache the read result for the implementation of AdvanceTo. It needs to be able to figure out how much
                // data got examined and consumed. It also needs to know if the reader is completed to mark reads as
                // completed on the stream.
                _readResult = result;
                if (result.Buffer.IsEmpty && result.IsCompleted)
                {
                    // Nothing to read and the writer is done, we can mark stream reads as completed now to release the
                    // stream count.
                    _stream.TrySetReadCompleted();
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        internal SlicPipeReader(
            SlicMultiplexedStream stream,
            MemoryPool<byte> pool,
            int minimumSegmentSize,
            int resumeThreshold,
            int pauseThreshold,
            Func<Memory<byte>, CancellationToken, ValueTask> readUntilFullFunc)
        {
            _stream = stream;
            _resumeThreshold = resumeThreshold;
            _readUntilFullFunc = readUntilFullFunc;
            _receiveCredit = pauseThreshold;
            _pipe = new(new PipeOptions(
                pool: pool,
                pauseWriterThreshold: 0,
                minimumSegmentSize: minimumSegmentSize,
                writerScheduler: PipeScheduler.Inline));
        }

        /// <summary>Called when a stream reset is received.</summary>
        internal void ReceivedResetFrame(long error)
        {
            if (_state.TrySetFlag(State.PipeWriterCompleted))
            {
                _exception = error.ToSlicError() == SlicStreamError.NoError ?
                    null :
                    new MultiplexedStreamAbortedException(error);

                if (!_state.HasFlag(State.PipeWriterInUse))
                {
                    _pipe.Writer.Complete(_exception);
                }
            }
        }

        /// <summary>Called when a stream frame is received. It writes the data from the received stream frame to the
        /// internal pipe writer and returns the number of bytes that were consumed.</summary>
        internal async ValueTask<int> ReceivedStreamFrameAsync(int dataSize, bool endStream, CancellationToken cancel)
        {
            if (dataSize == 0 && !endStream)
            {
                throw new InvalidDataException("empty stream frame are not allowed unless endStream is true");
            }

            _state.SetState(State.PipeWriterInUse);
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
                    throw new InvalidDataException("received more data than flow control permits");
                }

                // Read and append the received data to the pipe writer.
                int size = 0;
                while (size < dataSize)
                {
                    // Receive the data and push it to the pipe writer.
                    Memory<byte> chunk = _pipe.Writer.GetMemory();
                    chunk = chunk[0..Math.Min(dataSize - size, chunk.Length)];
                    await _readUntilFullFunc(chunk, cancel).ConfigureAwait(false);
                    size += chunk.Length;
                    _pipe.Writer.Advance(chunk.Length);

                    // Only flush if we didn't read all the data yet or if it's not the end of the stream. If it's the
                    // end of the stream and we've read all the data, we rely on CompleteAsync bellow to flush the data.
                    // This ensures the reader will get a read result with both the data and IsCompleted=true.
                    if (size < dataSize || !endStream)
                    {
                        _ = await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }

                if (endStream)
                {
                    // We complete the pipe writer but we don't mark reads as completed. Reads will be marked as
                    // completed once the application calls TryRead/ReadAsync. It's important for unidirectional stream
                    // which would otherwise be shutdown before the data has been consumed by the application. This
                    // would allow a malicious client to open many unidirectional streams before the application gets a
                    // chance to consume the data, defeating the purpose of the UnidirectionalStreamMaxCount option.
                    await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
                }

                return size;
            }
            finally
            {
                if (_state.HasFlag(State.PipeWriterCompleted))
                {
                    // If the Slic pipe reader has been completed while we were reader the data from the stream, we
                    // make sure to complete the writer now since Complete didn't do it.
                    await _pipe.Writer.CompleteAsync(_exception).ConfigureAwait(false);
                }
                _state.ClearFlag(State.PipeWriterInUse);
            }
        }

        private void CheckIfCompleted()
        {
            if (_state.HasFlag(State.Completed))
            {
                // If the reader is completed, the caller is bogus, it shouldn't call reader operations after completing
                // the pipe reader.
                throw new InvalidOperationException($"reading is not allowed once the reader is completed");
            }
        }

        /// <summary>The state enumeration is used to ensure the reader is not used after it's completed and to ensure
        /// that the internal pipe writer isn't completed concurrently when it's being used by <see
        /// cref="ReceivedStreamFrameAsync"/>.</summary>
        private enum State : int
        {
            /// <summary><see cref="Complete"/> was called on this Slic pipe reader.</summary>
            Completed = 1,
            /// <summary>Data is being written to the internal pipe writer.</summary>
            PipeWriterInUse = 2,
            /// <summary>The internal pipe writer was completed either by <see cref="Complete"/> or <see
            /// cref="ReceivedResetFrame"/>.</summary>
            PipeWriterCompleted = 4
        }
    }
}
