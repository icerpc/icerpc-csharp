// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    internal class SlicPipeReader : PipeReader
    {
        private int _examined;
        private Exception? _exception;
        private long _lastExaminedOffset;
        private readonly Pipe _pipe;
        private readonly Func<Memory<byte>, CancellationToken, ValueTask> _readFunc;
        private ReadResult _readResult;
        private readonly int _resumeThreshold;
        private int _state;
        private readonly SlicMultiplexedStream _stream;

        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            CheckIfCompleted();

            if (_lastExaminedOffset == 0)
            {
                _lastExaminedOffset = _readResult.Buffer.GetOffset(_readResult.Buffer.Start);
            }

            // Figure out how much data was examined since last AdvanceTo call.
            long examinedOffset = _readResult.Buffer.GetOffset(examined);
            int examinedLength = (int)(examinedOffset - _lastExaminedOffset);

            // If all the examined data has been consumed, the next pipe ReadAsync call will start reading from a new
            // buffer. In this case, we reset _lastExaminedOffset to 0. The next AdvanceTo call will compute the
            // examined data length from the start of the buffer.
            long consumedOffset = _readResult.Buffer.GetOffset(consumed);
            _lastExaminedOffset = consumedOffset == examinedOffset ? 0 : examinedOffset;

            // Add the examined length to the total examined length. If it's larger than the resume threshold, send the
            // stream resume write frame to the peer to obtain additional data.
            _examined += examinedLength;
            if (_examined >= _resumeThreshold)
            {
                _stream.SendStreamResumeWrite(_examined);
                _examined = 0;
            }

            // If we reached the end of the sequence and the peer won't be sending additional data, we can mark reads as
            // completed on the stream.
            bool readsCompleted =
                _readResult.IsCompleted &&
                consumedOffset == _readResult.Buffer.GetOffset(_readResult.Buffer.End);

            _pipe.Reader.AdvanceTo(consumed, examined);
            if (readsCompleted)
            {
                _stream.TrySetReadCompleted();
            }
        }

        public override void CancelPendingRead() => _pipe.Reader.CancelPendingRead();

        public override void Complete(Exception? exception = null)
        {
            if (_state.TrySetState(State.Completed))
            {
                if (_readResult.IsCompleted)
                {
                    // If the peer is no longer sending data, just just mark reads as completed on the stream.
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
                if (_state.HasState(State.Writing))
                {
                    _exception = exception;
                }
                else
                {
                    _pipe.Writer.Complete(exception);
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
            Func<Memory<byte>, CancellationToken, ValueTask> readFunc)
        {
            _stream = stream;
            _resumeThreshold = resumeThreshold;
            _readFunc = readFunc;

            // We configure the pipe to pause writes once the Slic stream pause threshold + 1 is reached. The flush on
            // the pipe writer always complete synchronously unless the peer sends too much data. See the implementation
            // of ReceivedStreamFrameAsync below.
            _pipe = new(new PipeOptions(
                pool: pool,
                minimumSegmentSize: minimumSegmentSize,
                pauseWriterThreshold: pauseThreshold + 1,
                writerScheduler: PipeScheduler.Inline));
        }

        /// <summary>Called when a stream reset is received.</summary>
        internal void ReceivedResetFrame(long error)
        {
            if (error.ToSlicError() == SlicStreamError.NoError)
            {
                _pipe.Writer.Complete();
            }
            else
            {
                _pipe.Writer.Complete(new MultiplexedStreamAbortedException(error));
            }
        }

        /// <summary>Called when a stream frame is received. It writes the data from the received stream frame to the
        /// internal pipe writer.</summary>
        internal async ValueTask<int> ReceivedStreamFrameAsync(int dataSize, bool endStream)
        {
            _state.SetState(State.Writing);
            try
            {
                if (_state.HasState(State.Completed))
                {
                    // If the Slic pipe reader is completed, skip the data.
                    return 0;
                }

                // Read and append the received data to the pipe writer.
                int size = dataSize;
                while (size > 0)
                {
                    // Receive the data and push it to the pipe writer.
                    Memory<byte> chunk = _pipe.Writer.GetMemory();
                    chunk = chunk[0..Math.Min(size, chunk.Length)];
                    await _readFunc(chunk, CancellationToken.None).ConfigureAwait(false);
                    size -= chunk.Length;
                    _pipe.Writer.Advance(chunk.Length);

                    // This should always complete synchronously since the sender isn't supposed to send more data than
                    // it is allowed. In other words, if the flush blocks because the PauseWriterThreshold is reached,
                    // the peer sent more data than it should have.
                    ValueTask<FlushResult> flushTask = _pipe.Writer.FlushAsync(CancellationToken.None);
                    if (!flushTask.IsCompleted)
                    {
                        _ = flushTask.AsTask();
                        throw new InvalidDataException("received more data than flow control permits");
                    }

                    try
                    {
                        FlushResult flushResult = await flushTask.ConfigureAwait(false);
                        if (flushResult.IsCompleted)
                        {
                            // The reader is completed, return the number of bytes left to skip.
                            return dataSize - size;
                        }
                    }
                    catch
                    {
                        // The reader is completed, return the number of bytes left to skip.
                        return dataSize - size;
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

                return dataSize;
            }
            finally
            {
                if (_state.HasState(State.Completed))
                {
                    await _pipe.Writer.CompleteAsync(_exception).ConfigureAwait(false);
                }
                _state.ClearState(State.Writing);
            }
        }

        private void CheckIfCompleted()
        {
            if (_state.HasState(State.Completed))
            {
                // If the reader is completed, the caller is bogus, it shouldn't call reader operations after completing
                // the pipe reader.
                throw new InvalidOperationException($"reading is not allowed once the reader is completed");
            }
        }

        private enum State : int
        {
            Writing = 1,
            Completed = 2,
        }
    }
}
