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
        /// <summary>Returns the number of bytes stored in the buffer.</summary>
        private int Count
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
        private bool _full = false;
        private int _head = 0;
        // The lock provides thread-safety for the _head, _full and _tail data members.
        private SpinLock _lock = new();
        private int _tail = 0;

        public void Dispose() => _bufferOwner.Dispose();

        /// <summary>Construct a new Circular buffer with the given capacity.</summary>
        /// <param name="capacity">The capacity of the buffer.</param>
        /// <exception cref="ArgumentOutOfRangeException">Raised if capacity is inferior to 1</exception>
        internal CircularBuffer(int capacity)
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
        internal Memory<byte> Enqueue(int size)
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

        /// <summary>Consumes data from the buffer. The data is copied to the given buffer and removed from this
        /// circular buffer. The caller must ensure that there's enough data available.</summary>
        /// <param name="buffer">The buffer to copy the consumed data to.</param>
        /// <exception cref="ArgumentOutOfRangeException">Raised the buffer is empty or larger than the available data.
        /// </exception>
        internal void Consume(Memory<byte> buffer)
        {
            if (buffer.IsEmpty)
            {
                throw new ArgumentOutOfRangeException("empty buffer");
            }

            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);

                int count = Count;
                if (count == 0)
                {
                    throw new ArgumentOutOfRangeException("buffer is empty");
                }
                else if (buffer.Length > count)
                {
                    throw new ArgumentOutOfRangeException("not enough data available");
                }

                int offset = 0;
                while (offset < buffer.Length)
                {
                    // Remaining size that needs to be filled up in the buffer
                    int size = buffer.Length - offset;

                    Memory<byte> chunk;
                    if (_head < _tail)
                    {
                        count = Math.Min(_tail - _head, size);
                        chunk = _buffer[_head..(_head + count)];
                        _head += count;
                    }
                    else if (_head < _buffer.Length)
                    {
                        count = Math.Min(_buffer.Length - _head, size);
                        chunk = _buffer[_head..(_head + count)];
                        _head += count;
                    }
                    else
                    {
                        count = Math.Min(_tail, size);
                        chunk = _buffer[0..count];
                        _head = count;
                    }

                    Debug.Assert(chunk.Length <= buffer.Length);
                    chunk.CopyTo(buffer[offset..]);
                    offset += chunk.Length;

                    _full = false;
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
    }
}
