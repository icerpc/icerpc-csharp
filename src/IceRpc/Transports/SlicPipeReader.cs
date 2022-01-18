// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    internal class SlicPipeReader : PipeReader
    {
        private int _consumed;
        private bool _isReaderCompleted;
        private readonly PipeReader _reader;
        private bool _readCompleted;
        private ReadOnlySequence<byte> _readSequence;
        private readonly int _resumeThreeshold;
        private readonly SlicMultiplexedStream _stream;

        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            CheckIfCompleted();

            // Figure out how much data has been consumed and add it to _consumed.
            long consumedOffset = _readSequence.GetOffset(consumed);
            int length = (int)(consumedOffset - _readSequence.GetOffset(_readSequence.Start));
            if (Interlocked.Add(ref _consumed, length) >= _resumeThreeshold)
            {
                // Notify the peer that it can send additional data.
                _stream.SendStreamConsumed(Interlocked.Exchange(ref _consumed, 0));
            }

            bool readsCompleted = consumedOffset == _readSequence.GetOffset(_readSequence.End) && _readCompleted;
            _reader.AdvanceTo(consumed, examined);

            // If all the data has been consumed and the writer has been complete, we can mark the reads as completed on
            // the stream. This will eventually shutdown the streams if writes are also marked as completed.
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
                if (!_stream.ReadsCompleted)
                {
                    if (exception is null)
                    {
                        // Unlike SlicePipeWriter.Complete we can't gracefully complete the read side without calling
                        // AbortRead (which will send the StopSending frame to the peer). We use the error code -1 here
                        // to not conflict with protocol error codes. TODO: Add a STOP_RECEIVING frame instead to notify
                        // the peer of the graceful writer completion?
                        _stream.AbortRead(-1);
                    }
                    else if (exception is MultiplexedStreamAbortedException abortedException)
                    {
                        _stream.AbortRead(abortedException.ErrorCode);
                    }
                    else
                    {
                        Debug.Assert(false, $"unexpected exception ${exception}");
                        throw new InvalidOperationException("invalid complete exception", exception);
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
            _readCompleted = result.IsCompleted;
            _readSequence = result.Buffer;
            return result;
        }

        public override bool TryRead(out ReadResult result)
        {
            CheckIfCompleted();
            if (_reader.TryRead(out result))
            {
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
                // if (_stream.IsShutdown)
                // {
                //     throw new ObjectDisposedException($"{typeof(IMultiplexedStream)}:{this}");
                // }

                // If the reader is completed, the caller is bogus, it shouldn't call reader operations after completing
                // the pipe reader.
                throw new InvalidOperationException("reading is not allowed once the reader is completed");
            }
        }
    }
}
