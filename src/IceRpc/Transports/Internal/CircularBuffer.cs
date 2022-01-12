// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;

namespace IceRpc.Transports.Internal
{
    /// <summary>A circular byte buffer class to buffer streamed data. The connection adds data to this buffer when
    /// receiving stream frames. The data is consumed from the buffer when the application reads the data from
    /// the stream. There can only be a single consumer and producer.</summary>
    internal struct CircularBuffer : IDisposable
    {
        public bool IsEmpty => Count == 0;

        public int Capacity => _buffer.Length;

        /// <summary>Returns the number of bytes stored in the buffer.</summary>
        public int Count
        {
            get
            {
                // We assume that the caller knows that there's data to consume. So if count == 0, this implies
                // that the buffer is full. The available data for consume is therefore _buffer.Length.
                if (_full)
                {
                    return _buffer.Length;
                }
                int count = _tail - _head;
                return count < 0 ? _buffer.Length + count : count;
            }
        }

        private readonly Memory<byte> _buffer;
        private readonly IMemoryOwner<byte> _bufferOwner;
        // _full is required to figure out whether or not the buffer is full or empty when _tail == _head.
        private bool _full;
        private int _head;
        // The lock provides thread-safety for the _head, _full and _tail data members.
        private SpinLock _lock;
        private int _tail;
        private SequenceSegment? _firstSegment;
        private SequenceSegment? _secondSegment;

        public void Dispose() => _bufferOwner.Dispose();

        /// <summary>Construct a new Circular buffer with the given capacity.</summary>
        /// <param name="capacity">The capacity of the buffer.</param>
        /// <exception cref="ArgumentOutOfRangeException">Raised if capacity is inferior to 1</exception>
        internal CircularBuffer(int capacity)
            : this()
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException("capacity can't be < 1");
            }
            _bufferOwner = MemoryPool<byte>.Shared.Rent(capacity);
            _buffer = _bufferOwner.Memory[0..capacity];
        }

        /// <summary>Add data to the buffer. This method doesn't actually copy the data to the buffer but returns a
        /// slice of the buffer of the given size. The producer is responsible for filling the data in. The buffer is
        /// typically used to receive the data from the connection. The caller must ensure there's enough space for
        /// adding the data.</summary>
        /// <param name="size">The size of the data to add.</param>
        /// <return>A buffer of the given size.</return>
        /// <exception cref="ArgumentOutOfRangeException">Raised if size if superior to the available space or inferior
        /// to one byte.</exception>
        internal Memory<byte> GetWriteBuffer(int size)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException("size must be superior to 0");
            }

            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                if (size > _buffer.Length - Count)
                {
                    throw new ArgumentOutOfRangeException("not enough space available");
                }

                Memory<byte> chunk;
                if (_head > _tail)
                {
                    int count = Math.Min(_head - _tail, size);
                    chunk = _buffer[_tail..(_tail + count)];
                    _tail += count;
                }
                else if (_tail < _buffer.Length)
                {
                    int count = Math.Min(_buffer.Length - _tail, size);
                    chunk = _buffer[_tail..(_tail + count)];
                    _tail += count;
                }
                else
                {
                    int count = Math.Min(_head, size);
                    chunk = _buffer[0..count];
                    _tail = count;
                }
                _full = _tail == _head;
                return chunk;
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }

        internal ReadOnlySequence<byte> GetReadBuffer()
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);

                if (_head <= _tail && !_full)
                {
                    return new ReadOnlySequence<byte>(_buffer[_head.._tail]);
                }
                else if (_head < _buffer.Length)
                {
                    _secondSegment ??= new SequenceSegment();
                    _firstSegment ??= new SequenceSegment(_secondSegment);
                    _firstSegment.SetMemory(_buffer[_head.._buffer.Length]);
                    _secondSegment.SetMemory(_buffer[0.._tail]);
                    return new ReadOnlySequence<byte>(_firstSegment, 0, _secondSegment, _tail);
                }
                else
                {
                    return new ReadOnlySequence<byte>(_buffer[0.._tail]);
                }
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }

        /// <summary>Consumes data from the buffer. The data is copied to the given buffer and removed from this
        /// circular buffer. The caller must ensure that there's enough data available.</summary>
        /// <param name="consumed">The buffer to copy the consumed data to.</param>
        /// <exception cref="ArgumentOutOfRangeException">Raised the buffer is empty or larger than the available data.
        /// </exception>
        internal int AdvanceTo(SequencePosition consumed)
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);

                int position = consumed.GetInteger();
                int size;

                if (_head < position)
                {
                    size = position - _head;
                }
                else if (_head == position && !_full)
                {
                    size = 0;
                }
                else
                {
                    size = _buffer.Length - _head + position;
                }

                if (size > Count)
                {
                    throw new ArgumentOutOfRangeException($"can't consume more bytes ({size}) than available");
                }

                _head = position;
                _full = false;
                return size;
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }

        private sealed class SequenceSegment : ReadOnlySequenceSegment<byte>
        {
            internal SequenceSegment(SequenceSegment? next = null) => Next = next;

            internal void SetMemory(ReadOnlyMemory<byte> memory) => Memory = memory;
        }
    }
}
