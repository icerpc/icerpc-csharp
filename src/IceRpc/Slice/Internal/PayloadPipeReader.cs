// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice.Internal
{
    internal sealed class PayloadPipeReader : PipeReader
    {
        private bool _isReaderCompleted;
        private readonly IDisposable _disposable;
        private ReadOnlySequence<byte> _sequence;

        public override bool TryRead(out ReadResult result)
        {
            if (_isReaderCompleted)
            {
                throw new InvalidOperationException("reader completed");
            }
            result = new ReadResult(_sequence, isCanceled: false, isCompleted: true);
            return true;
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            TryRead(out ReadResult result);
            return new(result);
        }

        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            if (_isReaderCompleted)
            {
                throw new InvalidOperationException("reader completed");
            }
            _sequence = _sequence.Slice(consumed);
        }

        public override void CancelPendingRead()
        {
        }

        public override void Complete(Exception? exception = null)
        {
            if (!_isReaderCompleted)
            {
                _isReaderCompleted = true;
                _disposable.Dispose();
            }
        }

        internal PayloadPipeReader(SequenceBufferWriter bufferWriter) :
            this(bufferWriter.Sequence, bufferWriter)
        {

        }

        internal PayloadPipeReader(ReadOnlySequence<byte> sequence, IDisposable disposable)
        {
            _disposable = disposable;
            _sequence = sequence;
        }
    }
}
