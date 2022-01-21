// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    internal class SlicPipeReader : PipeReader
    {
        private int _examined;
        private bool _isReaderCompleted;
        private long _lastExaminedOffset;
        private readonly PipeReader _reader;
        private bool _readCompleted;
        private ReadOnlySequence<byte> _readSequence;
        private readonly int _resumeThreeshold;
        private readonly SlicMultiplexedStream _stream;

        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            CheckIfCompleted();

            if (_lastExaminedOffset == 0)
            {
                _lastExaminedOffset = _readSequence.GetOffset(_readSequence.Start);
            }

            // Figure out how much data was examined since last AdvanceTo call.
            long examinedOffset = _readSequence.GetOffset(examined);
            int examinedLength = (int)(examinedOffset - _lastExaminedOffset);

            // If all the examined data has been consumed, the next pipe ReadAsync call will start reading from a new
            // buffer. In this case, we reset _lastExaminedOffset to 0. The next AdvanceTo call will compute the
            // examined data length from the start of the buffer.
            long consumedOffset = _readSequence.GetOffset(consumed);
            _lastExaminedOffset = consumedOffset == examinedOffset ? 0 : examinedOffset;

            // Add the examined length to the total examined length. If it's larger than the resume threeshold, send the
            // stream consumed frame to the peer to obtain additional data and reset the total examined length.
            _examined += examinedLength;
            if (_examined >= _resumeThreeshold)
            {
                _stream.SendStreamConsumed(_examined);
                _examined = 0;
            }

            // Check if we reached the end of the sequence.
            bool endOfSequence = consumedOffset == _readSequence.GetOffset(_readSequence.End);

            _reader.AdvanceTo(consumed, examined);

            // If we reached the end of the sequence and we peer won't be sending additional data, we can mark reads
            // as completed on the stream.
            if (endOfSequence && _readCompleted)
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
                    if (exception is null)
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
            _readCompleted = result.IsCompleted;
            _readSequence = result.Buffer;
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
                _readCompleted = result.IsCompleted;
                _readSequence = result.Buffer;
                return true;
            }
            else
            {
                return false;
            }
        }

        internal SlicPipeReader(SlicMultiplexedStream stream, PipeReader reader, int resumeThreeshold)
        {
            _stream = stream;
            _reader = reader;
            _resumeThreeshold = resumeThreeshold;
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
