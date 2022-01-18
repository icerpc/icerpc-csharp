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

        private SequenceSegment? _start;
        private SequenceSegment? _end;
        private ReadOnlySequence<byte>? _sequence;

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

        public Memory<byte> GetMemory(int sizeHint)
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
            if (buffer.Length > sizeHint)
            {
                return buffer;
            }
            else
            {
                // Add new segment if there's not enough space in the last segment.
                _end = new SequenceSegment(_end, sizeHint);
                return _end.GetMemory();
            }
        }

        public Span<byte> GetSpan(int sizeHint) => GetMemory(sizeHint).Span;

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
                _owner = MemoryPool<byte>.Shared.Rent(sizeHint);
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
    }
}
