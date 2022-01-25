// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    internal class SlicPipeReader : PipeReader
    {
        private int _examined;
        private bool _isReaderCompleted;
        private long _lastExaminedOffset;
        private readonly PipeReader _reader;
        private ReadResult _readResult;
        private readonly int _resumeThreshold;
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

            // If we reached the end of the sequence and the peer won't be sending additional data, we can mark reads
            // as completed on the stream.
            bool readsCompleted =
                _readResult.IsCompleted &&
                consumedOffset == _readResult.Buffer.GetOffset(_readResult.Buffer.End);

            _reader.AdvanceTo(consumed, examined);

            if (readsCompleted)
            {
                _stream.TrySetReadCompleted();
            }
        }

        public override void CancelPendingRead() => _reader.CancelPendingRead();

        public override void Complete(Exception? exception = null)
        {
            if (!_isReaderCompleted)
            {
                // If reads aren't marked as completed yet, abort stream reads. This will send a stream stop sending
                // frame to the peer to notify it shouldn't send additional data.
                if (!_stream.ReadsCompleted)
                {
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

                _isReaderCompleted = true;

                _reader.Complete(exception);
            }
        }

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancel = default)
        {
            CheckIfCompleted();
            ReadResult result = await _reader.ReadAsync(cancel).ConfigureAwait(false);
            if (result.IsCanceled && _stream.ResetError is long error)
            {
                throw new MultiplexedStreamAbortedException(error);
            }

            // Cache the read result for the implementation of AdvanceTo. It needs to be able to figure out how much
            // data got examined and consumed. It also needs to know if the reader is completed to mark reads as
            // completed on the stream. We can't do this here as it could cause the stream shutdown to complete this
            // Slice pipe reader if the stream write side is also completed.
            _readResult = result;
            return result;
        }

        public override bool TryRead(out ReadResult result)
        {
            CheckIfCompleted();
            if (_reader.TryRead(out result))
            {
                if (result.IsCanceled && _stream.ResetError is long error)
                {
                    throw new MultiplexedStreamAbortedException(error);
                }

                // Cache the read result for the implementation of AdvanceTo. It needs to be able to figure out how much
                // data got examined and consumed. It also needs to know if the reader is completed to mark reads as
                // completed on the stream. We can't do this here as it could cause the stream shutdown to complete this
                // Slice pipe reader if the stream write side is also completed.
                _readResult = result;
                return true;
            }
            else
            {
                return false;
            }
        }

        internal SlicPipeReader(SlicMultiplexedStream stream, PipeReader reader, int resumeThreshold)
        {
            _stream = stream;
            _reader = reader;
            _resumeThreshold = resumeThreshold;
        }

        private void CheckIfCompleted()
        {
            if (_isReaderCompleted)
            {
                // If the reader is completed, the caller is bogus, it shouldn't call reader operations after completing
                // the pipe reader.
                throw new InvalidOperationException($"reading is not allowed once the reader is completed");
            }
        }
    }
}
