// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Diagnostics;
using System.Threading;

namespace ZeroC.Ice
{
    /// <summary>A circular byte buffer class to buffer streamed data. The socket adds data to this buffer when
    /// receiving stream frames. The data is consumed from the buffer when the application reads the data from
    /// the stream. There can only be a single consumer and producer.</summary>
    internal class CircularBuffer
    {
        /// <summary>Returns the number of bytes which can be added to the buffer.</summary>
        internal int Available => Capacity - Count;

        /// <summary>Returns the buffer capacity.</summary>
        internal int Capacity => _buffer.Length;

        /// <summary>Returns the number of bytes stored in the buffer.</summary>
        internal int Count
        {
            get
            {
                // We assume that the caller knows that there's data to consume. So if count == 0, this implies
                // that the buffer is full. The available data for consume is therefore _buffer.Length.
                bool lockTaken = false;
                try
                {
                    _lock.Enter(ref lockTaken);
                    if (_full)
                    {
                        return _buffer.Length;
                    }
                    int count = _tail - _head;
                    return count < 0 ? _buffer.Length + count : count;
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

        private readonly byte[] _buffer;
        // _full is required to figure out whether or not the buffer is full or empty when _tail == _head.
        private bool _full;
        private int _head;
        // The lock provides thread-safety for the _head, _full and _tail data members.
        private SpinLock _lock;
        private int _tail;

        /// <summary>Construct a new Circular buffer with the given capacity.</summary>
        /// <param name="capacity">The capacity of the buffer.</param>
        internal CircularBuffer(int capacity) => _buffer = new byte[capacity];

        /// <summary>Add data to the buffer. This method doesn't actually copy the data to the buffer but returns
        /// a slice of the buffer of the given size. The producer is responsible for filling the data in. The
        /// buffer is typically used to receive the data from the socket. The caller must ensure there's enough
        /// space for adding the data.</summary>
        /// <param name="size">The size of the data to add.</param>
        /// <return>A buffer of the given size.</return>
        internal Memory<byte> Enqueue(int size)
        {
            Debug.Assert(size <= Available, "buffer is full");

            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                Memory<byte> segment;
                if (_head > _tail)
                {
                    int count = Math.Min(_head - _tail, size);
                    segment = new(_buffer, _tail, count);
                    _tail += count;
                }
                else if (_tail < _buffer.Length)
                {
                    int count = Math.Min(_buffer.Length - _tail, size);
                    segment = new(_buffer, _tail, count);
                    _tail += count;
                }
                else
                {
                    int count = Math.Min(_head, size);
                    segment = new(_buffer, 0, count);
                    _tail = count;
                }
                _full = _tail == _head;
                return segment;
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }

        /// <summary>Consumes data from the buffer. The data is copied to the given buffer and removed from
        /// this circular buffer. The caller must ensure that there's enough data available.</summary>
        /// <param name="buffer">The buffer to copy the consumed data to.</param>
        internal void Consume(Memory<byte> buffer)
        {
            Debug.Assert(buffer.Length <= Count, "not enough data available");
            Debug.Assert(buffer.Length > 0, "empty buffer");

            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                int offset = 0;
                while (offset < buffer.Length)
                {
                    // Remaining size that needs to be filled up in the buffer
                    int size = buffer.Length - offset;

                    Memory<byte> segment;
                    if (_head < _tail)
                    {
                        int count = Math.Min(_tail - _head, size);
                        segment = new(_buffer, _head, count);
                        _head += count;
                    }
                    else if (_head < _buffer.Length)
                    {
                        int count = Math.Min(_buffer.Length - _head, size);
                        segment = new(_buffer, _head, count);
                        _head += count;
                    }
                    else
                    {
                        int count = Math.Min(_tail, size);
                        segment = new(_buffer, 0, count);
                        _head = count;
                    }

                    Debug.Assert(segment.Length <= buffer.Length);
                    segment.CopyTo(buffer[offset..]);
                    offset += segment.Length;

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
