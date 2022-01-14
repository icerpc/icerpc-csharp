// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    internal class SlicPipeReader : PipeReader
    {
        private int _consumed;
        private readonly PipeReader _reader;
        private ReadOnlySequence<byte> _readSequence;
        private readonly int _resumeThreeshold;
        private readonly SlicMultiplexedStream _stream;

        public override bool TryRead(out ReadResult result)
        {
            CheckIfReadsCompleted();
            if (_reader.TryRead(out result))
            {
                if (result.IsCompleted)
                {
                    _stream.TrySetReadCompleted();
                }
                _readSequence = result.Buffer;
                return true;
            }
            else
            {
                return false;
            }
        }

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancel = default)
        {
            CheckIfReadsCompleted();
            ReadResult result = await _reader.ReadAsync(cancel).ConfigureAwait(false);
            if (result.IsCompleted)
            {
                _stream.TrySetReadCompleted();
            }
            _readSequence = result.Buffer;
            return result;
        }

        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            CheckIfReadsCompleted();

            // Figure out how much data has been consumed and add it to _consumed.
            int length = (int)(_readSequence.GetOffset(consumed) - _readSequence.GetOffset(_readSequence.Start));
            if (Interlocked.Add(ref _consumed, length) >= _resumeThreeshold)
            {
                // Notify the peer that it can send additional data.
                _stream.SendStreamConsumed(Interlocked.Exchange(ref _consumed, 0));
            }

            _reader.AdvanceTo(consumed, examined);
        }

        public override void CancelPendingRead() => _reader.CancelPendingRead();

        public override void Complete(Exception? exception = null)
        {
            if (!_stream.ReadsCompleted)
            {
                if (exception is null)
                {
                    // Unlike SlicePipeWriter.Complete we can't gracefully complete the read side without calling
                    // AbortRead (which will send the StopSending frame to the peer). We use the error code -1 here to
                    // not conflict with protocol error codes.
                    _stream.AbortRead(-1);
                }
                else if (exception is MultiplexedStreamAbortedException abortedException)
                {
                    _stream.AbortRead(abortedException.ErrorCode);
                }
                else
                {
                    throw new InvalidOperationException("unexpected Complete exception", exception);
                }
            }

            _reader.Complete(exception);
        }

        internal SlicPipeReader(SlicMultiplexedStream stream, PipeReader reader, int resumeThreeshold)
        {
            _stream = stream;
            _reader = reader;
            _resumeThreeshold = resumeThreeshold;
        }

        private void CheckIfReadsCompleted()
        {
            if (_stream.IsShutdown)
            {
                throw new ObjectDisposedException($"{typeof(IMultiplexedStream)}:{this}");
            }
            else if (_stream.ReadsCompleted)
            {
                if (_stream.ResetErrorCode is long resetErrorCode)
                {
                    throw new MultiplexedStreamAbortedException(resetErrorCode);
                }
                else
                {
                    // If reads completed normally, the caller is bogus, it shouldn't call ReadAsync after completing
                    // the pipe reader.
                    throw new InvalidOperationException("reading is not allowed after reader was completed");
                }
            }
        }
    }
}
