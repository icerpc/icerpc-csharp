// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using System.Diagnostics;

namespace IceRpc.Slice.Internal
{
    /// <summary>The payload pipe reader is used both as a writer to encode a Slice payload and as a pipe reader to
    /// read the encoded payload.</summary>
#pragma warning disable CA1001 // Complete dispose _end
    internal sealed class PayloadPipeReader : PipeReader, IBufferWriter<byte>
#pragma warning restore CA1001
    {
        private sealed class SequenceSegment : ReadOnlySequenceSegment<byte>, IDisposable
        {
            private readonly IMemoryOwner<byte> _owner;
            private int _written;

            public void Dispose() => _owner.Dispose();

            internal SequenceSegment(int size)
            {
                RunningIndex = 0;
                _owner = MemoryPool<byte>.Shared.Rent(size);
            }

            internal SequenceSegment(SequenceSegment segment)
            {
                segment.Complete();
                segment.Next = this;

                RunningIndex = segment.RunningIndex + segment.Memory.Length;
                _owner = MemoryPool<byte>.Shared.Rent(segment._owner.Memory.Length * 2);
            }

            internal void Advance(int written) => _written += written;

            internal void Complete() => Memory = _owner.Memory[0.._written];

            internal Memory<byte> GetMemory() => _owner.Memory[_written..];
        }

        private const int DefaultBufferSize = 256;
        private readonly SequenceSegment _start;
        private SequenceSegment _end;
        private bool _isReaderCompleted;
        private ReadOnlySequence<byte>? _sequence;

        public override bool TryRead(out ReadResult result)
        {
            if (_isReaderCompleted)
            {
                throw new InvalidOperationException("reader completed");
            }
            _sequence ??= new ReadOnlySequence<byte>(_start, 0, _end, _end.Memory.Length);
            result = new ReadResult(_sequence.Value, isCanceled: false, isCompleted: true);
            return true;
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (!TryRead(out ReadResult result))
            {
                result = new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true);
            }
            return new(result);
        }

        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            if (_isReaderCompleted)
            {
                throw new InvalidOperationException("reader completed");
            }
            if (_sequence == null)
            {
                throw new InvalidOperationException($"{nameof(ReadAsync)} or {nameof(TryRead)} must be called first");
            }

            if (consumed.Equals(_sequence.Value.End))
            {
                _sequence = ReadOnlySequence<byte>.Empty;
                return;
            }
            _sequence = _sequence.Value.Slice(consumed);
        }

        public override void CancelPendingRead()
        {
        }

        public override void Complete(Exception? exception = null)
        {
            if (!_isReaderCompleted)
            {
                _isReaderCompleted = true;
                for (SequenceSegment segment = _start; segment.Next != null; segment = (SequenceSegment)segment.Next)
                {
                    segment.Dispose();
                }
            }
        }

        void IBufferWriter<byte>.Advance(int count)
        {
            if (_sequence != null)
            {
                throw new InvalidOperationException("can't write once reading started");
            }
            if (count < 0)
            {
                throw new ArgumentException(null, nameof(count));
            }
            _end.Advance(count);
        }

        Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint)
        {
            if (_sequence != null)
            {
                throw new InvalidOperationException("can't write once reading started");
            }

            // Add new segment if there's not enough space in the last segment.
            if (_end.GetMemory().Length < sizeHint)
            {
                _end = new SequenceSegment(_end);
                Debug.Assert(_end.GetMemory().Length > sizeHint);
            }
            return _end.GetMemory();
        }

        Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint) => ((IBufferWriter<byte>)this).GetMemory(sizeHint).Span;

        internal PayloadPipeReader() => _start = _end = new SequenceSegment(DefaultBufferSize);
    }
}
