// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using System.Diagnostics;

namespace IceRpc.Slice.Internal
{
    /// <summary>The payload pipe reader is used both as a writer to encode a Slice payload and as a pipe reader to
    /// read the encoded payload. Once reading starts, no more data can be written.</summary>
#pragma warning disable CA1001 // Complete disposes _end
    internal sealed class PayloadPipeReader : PipeReader, IBufferWriter<byte>
#pragma warning restore CA1001
    {
        /// <summary>A sequence segment is first used to write data and then used the create the readonly sequence
        /// used for the pipe reader.</summary>
        private sealed class SequenceSegment : ReadOnlySequenceSegment<byte>, IDisposable
        {
            internal int Capacity => _owner.Memory.Length;

            private int _index;
            private readonly IMemoryOwner<byte> _owner;

            public void Dispose() => _owner.Dispose();

            internal SequenceSegment(int sizeHint)
            {
                RunningIndex = 0;
                _owner = MemoryPool<byte>.Shared.Rent(Math.Max(sizeHint, DefaultBufferSize));
            }

            internal SequenceSegment(SequenceSegment previous, int size)
            {
                // Complete writes on the previous segment.
                previous.Complete();
                previous.Next = this;

                // RunningIndex is set to the RunningIndex of the previous segment plus its size. It's used by
                // the ReadOnlySequence to figure out its length.
                RunningIndex = previous.RunningIndex + previous.Memory.Length;

                // Allocate a buffer which is twice the size of the previous segment buffer.
                _owner = MemoryPool<byte>.Shared.Rent(size);
            }

            internal void Advance(int written) => _index += written;

            internal void Complete() => Memory = _owner.Memory[0.._index];

            internal Memory<byte> GetMemory() => _owner.Memory[_index..];
        }

        private const int DefaultBufferSize = 256;
        private SequenceSegment? _start;
        private SequenceSegment? _end;
        private bool _isReaderCompleted;
        private ReadOnlySequence<byte>? _sequence;

        public override bool TryRead(out ReadResult result)
        {
            if (_isReaderCompleted)
            {
                throw new InvalidOperationException("reader completed");
            }

            if (_sequence == null)
            {
                if (_end == null)
                {
                    _sequence = ReadOnlySequence<byte>.Empty;
                }
                else
                {
                    Debug.Assert(_start != null);
                    _end.Complete();
                    _sequence = new ReadOnlySequence<byte>(_start, 0, _end, _end.Memory.Length);
                }
            }
            result = new ReadResult(_sequence.Value, isCanceled: false, isCompleted: true);
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
            if (_sequence == null)
            {
                throw new InvalidOperationException($"{nameof(ReadAsync)} or {nameof(TryRead)} must be called first");
            }

            if (consumed.Equals(_sequence.Value.End))
            {
                _sequence = ReadOnlySequence<byte>.Empty;
            }
            else
            {
                _sequence = _sequence.Value.Slice(consumed);
            }
        }

        public override void CancelPendingRead()
        {
        }

        public override void Complete(Exception? exception = null)
        {
            if (!_isReaderCompleted)
            {
                _isReaderCompleted = true;
                for (SequenceSegment? segment = _start; segment != null; segment = (SequenceSegment?)segment?.Next)
                {
                    segment?.Dispose();
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
            if (_end == null)
            {
                throw new InvalidOperationException($"{nameof(IBufferWriter<byte>.GetMemory)} must be called first");
            }
            _end.Advance(count);
        }

        Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint)
        {
            if (_sequence != null)
            {
                throw new InvalidOperationException("can't write once reading started");
            }

            if (_end == null)
            {
                _start = new SequenceSegment(sizeHint);
                _end = _start;
            }

            Memory<byte> buffer = _end.GetMemory();
            int sizeLeft = buffer.Length;
            if (sizeHint == 0)
            {
                sizeHint = sizeLeft > 0 ? sizeLeft : _end.Capacity;
            }

            // Add new segment if there's not enough space in the last segment.
            if (sizeLeft < sizeHint)
            {
                _end = new SequenceSegment(_end, Math.Max(sizeHint, _end.Capacity * 2));
                buffer = _end.GetMemory();
            }
            return buffer;
        }

        Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint) => ((IBufferWriter<byte>)this).GetMemory(sizeHint).Span;
    }
}
