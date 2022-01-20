// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;

namespace IceRpc.Internal
{
    internal sealed class SequenceBufferWriter : IBufferWriter<byte>, IDisposable
    {
        internal ReadOnlySequence<byte> Sequence
        {
            get
            {
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
                return _sequence.Value;
            }
        }

        private SequenceSegment? _end;
        private readonly int _minimumSegmentSize;
        private MemoryPool<byte> _pool;
        private ReadOnlySequence<byte>? _sequence;
        private SequenceSegment? _start;

        public void Advance(int count)
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

        public void Dispose()
        {
            for (SequenceSegment? segment = _start; segment != null; segment = (SequenceSegment?)segment?.Next)
            {
                segment?.Dispose();
            }
            _start?.Dispose();
            _end?.Dispose();
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (_sequence != null)
            {
                throw new InvalidOperationException("can't write once reading started");
            }

            int size = Math.Max(sizeHint, _minimumSegmentSize);

            if (_end == null)
            {
                _start = new SequenceSegment(_pool.Rent(size));
                _end = _start;
                return _end.GetMemory();
            }
            else
            {
                Memory<byte> buffer = _end.GetMemory();
                if (buffer.Length > sizeHint)
                {
                    return buffer;
                }
                else
                {
                    // Add new segment if there's not enough space in the last segment.
                    _end = new SequenceSegment(_end, _pool.Rent(size));
                    return _end.GetMemory();
                }
            }
        }

        public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;

        internal SequenceBufferWriter(MemoryPool<byte> pool, int minimumSegmentSize)
        {
            _pool = pool;
            _minimumSegmentSize = minimumSegmentSize;
        }

        /// <summary>A sequence segment is first used to write data and then used the create the readonly sequence
        /// used for the pipe reader.</summary>
        private sealed class SequenceSegment : ReadOnlySequenceSegment<byte>, IDisposable
        {
            internal int Capacity => _owner.Memory.Length;

            private int _index;
            private readonly IMemoryOwner<byte> _owner;

            public void Dispose() => _owner.Dispose();

            internal SequenceSegment(IMemoryOwner<byte> owner)
            {
                Debug.Assert(owner.Memory.Length > 0);
                RunningIndex = 0;
                _owner = owner;
            }

            internal SequenceSegment(SequenceSegment previous, IMemoryOwner<byte> owner)
            {
                Debug.Assert(owner.Memory.Length > 0);

                // Complete writes on the previous segment.
                previous.Complete();
                previous.Next = this;

                // RunningIndex is set to the RunningIndex of the previous segment plus its size. It's used by
                // the ReadOnlySequence to figure out its length.
                RunningIndex = previous.RunningIndex + previous.Memory.Length;

                _owner = owner;
            }

            internal void Advance(int written) => _index += written;

            internal void Complete() => Memory = _owner.Memory[0.._index];

            internal Memory<byte> GetMemory() => _owner.Memory[_index..];
        }
    }
}
