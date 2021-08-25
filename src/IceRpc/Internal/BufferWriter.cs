// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc.Internal
{
    /// <summary>Writes bytes into a non-contiguous byte buffer.</summary>
    internal class BufferWriter
    {
        /// <summary>Represents a position in the underlying buffer vector. This position consists of the index of the
        /// buffer in the vector and the offset into that buffer.</summary>
        internal struct Position : IEquatable<Position>
        {
            /// <summary>The zero based index of the buffer.</summary>
            internal int Buffer;

            /// <summary>The offset into the buffer.</summary>
            internal int Offset;

            /// <summary>Creates a new position from the buffer and offset values.</summary>
            /// <param name="buffer">The zero based index of the buffer in the buffer vector.</param>
            /// <param name="offset">The offset into the buffer.</param>
            public Position(int buffer, int offset)
            {
                Buffer = buffer;
                Offset = offset;
            }

            /// <inheritdoc/>
            public bool Equals(Position other) => Buffer == other.Buffer && Offset == other.Offset;

            /// <inheritdoc/>
            public override bool Equals(object? obj) => obj is Position value && Equals(value);

            /// <inheritdoc/>
            public override int GetHashCode() => HashCode.Combine(Buffer, Offset);

            /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
            public static bool operator ==(Position lhs, Position rhs) => lhs.Equals(rhs);

            /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
            public static bool operator !=(Position lhs, Position rhs) => !(lhs == rhs);
        }

        /// <summary>The number of bytes that the underlying buffer vector can hold without further allocation.
        /// </summary>
        internal int Capacity { get; private set; }

        /// <summary>Determines the current size of the buffer. This corresponds to the number of bytes already encoded
        /// using this encoder.</summary>
        /// <value>The current size.</value>
        internal int Size { get; private set; }

        /// <summary>Gets the position of the next write.</summary>
        internal Position Tail => _tail;

        private const int DefaultBufferSize = 256;

        // All buffers before the tail buffer are fully used.
        private Memory<ReadOnlyMemory<byte>> _bufferVector = Memory<ReadOnlyMemory<byte>>.Empty;

        // The buffer currently used by write operations. The tail Position always points to this buffer, and the tail
        // offset indicates how much of the buffer has been used.
        private Memory<byte> _currentBuffer;

        // The position for the next write operation.
        private Position _tail;

         // Constructs a BufferWriter
        internal BufferWriter(Memory<byte> initialBuffer = default)
        {
            _tail = default;
            Size = 0;
            _currentBuffer = initialBuffer;
            if (_currentBuffer.Length > 0)
            {
                _bufferVector = new ReadOnlyMemory<byte>[] { _currentBuffer };
            }

            Capacity = _currentBuffer.Length;
        }

        /// <summary>Add memory buffers to the underlying buffer vector. Small buffers are copied to the last
        /// buffer from the underlying vector if it's not full.</summary>
        /// <param name="buffers">The buffers to add.</param>
        internal void Add(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
        {
            // Coalesce small buffers at the end of the current buffer
            int index = 0;
            while (index < buffers.Length && buffers.Span[index].Length <= Capacity - Size)
            {
                WriteByteSpan(buffers.Span[index++].Span);
            }

            // Add remaining buffers if any
            if (index < buffers.Length)
            {
                // Terminate the last buffer.
                if (_bufferVector.Length > 0)
                {
                    _bufferVector.Span[^1] = _bufferVector.Span[^1].Slice(0, _tail.Offset);
                }

                var newBuffers = new ReadOnlyMemory<byte>[_bufferVector.Length + buffers.Length - index];
                _bufferVector.CopyTo(newBuffers);
                foreach (ReadOnlyMemory<byte> memory in buffers.Span[index..])
                {
                    newBuffers[_bufferVector.Length + index++] = memory;
                    Size += memory.Length;
                }
                _bufferVector = newBuffers;
                Capacity = Size;

                _tail.Buffer = _bufferVector.Length;
                _tail.Offset = _bufferVector.Span[^1].Length;

                // It's fine to set this to default when there's no space left in the buffer vector. It's only
                // used by Write methods that always call Expand first. Expand will re-assign _currentBuffer
                // where there's no space left.
                _currentBuffer = default;
            }
        }

        /// <summary>Returns the distance in bytes from start position to the tail position.</summary>
        /// <param name="start">The start position from where to calculate distance to the tail position.</param>
        /// <returns>The distance in bytes from the start position to the tail position.</returns>
        internal int Distance(Position start)
        {
            Debug.Assert(Tail.Buffer > start.Buffer ||
                        (Tail.Buffer == start.Buffer && Tail.Offset >= start.Offset));

            return Distance(_bufferVector, start, Tail);
        }

        /// <summary>Finishes off the underlying buffer vector and returns it. You should not write additional data to
        /// this buffer after calling Finish, however rewriting previous data is fine.</summary>
        /// <returns>The buffers.</returns>
        internal ReadOnlyMemory<ReadOnlyMemory<byte>> Finish()
        {
            if (_bufferVector.Length > 0)
            {
                _bufferVector.Span[^1] = _bufferVector.Span[^1].Slice(0, _tail.Offset);
            }
            return _bufferVector;
        }

        /// <summary>Rewrites a single byte at a given position.</summary>
        /// <param name="v">The byte value to write.</param>
        /// <param name="pos">The position to write to.</param>
        internal void RewriteByte(byte v, Position pos)
        {
            Memory<byte> buffer = GetBuffer(pos.Buffer);

            if (pos.Offset < buffer.Length)
            {
                buffer.Span[pos.Offset] = v;
            }
            else
            {
                // (segN, segN.Count) points to the same byte as (segN + 1, 0)
                Debug.Assert(pos.Offset == buffer.Length);
                buffer = GetBuffer(pos.Buffer + 1);
                buffer.Span[0] = v;
            }
        }

        /// <summary>Rewrites a span of bytes.</summary>
        /// <param name="data">The data to write as a span of bytes.</param>
        /// <param name="pos">The position to write these bytes.</param>
        /// <remarks>These bytes must have been written with <see cref="WriteByteSpan"/> and as a result can span at
        /// most two segments</remarks>
        internal void RewriteByteSpan(ReadOnlySpan<byte> data, Position pos)
        {
            Memory<byte> buffer = GetBuffer(pos.Buffer);

            int remaining = Math.Min(data.Length, buffer.Length - pos.Offset);
            if (remaining > 0)
            {
                data.Slice(0, remaining).CopyTo(buffer.Span.Slice(pos.Offset, remaining));
            }

            if (remaining < data.Length)
            {
                buffer = GetBuffer(pos.Buffer + 1);
                data[remaining..].CopyTo(buffer.Span.Slice(0, data.Length - remaining));
            }
        }

        /// <summary>Writes a sequence of bits and returns this sequence backed by the buffer.</summary>
        /// <param name="bitSize">The minimum number of bits in the sequence.</param>
        /// <returns>The bit sequence, with all bits set. The actual size of the sequence is a multiple of 8.
        /// </returns>
        internal BitSequence WriteBitSequence(int bitSize)
        {
            Debug.Assert(bitSize > 0);
            int size = (bitSize >> 3) + ((bitSize & 0x07) != 0 ? 1 : 0);
            Expand(size);

            int remaining = _currentBuffer.Length - _tail.Offset;
            if (size <= remaining)
            {
                // Expand above ensures _tail.Offset is not _currentBuffer.Count.
                Span<byte> span = _currentBuffer.Span.Slice(_tail.Offset, size);
                span.Fill(255);
                _tail.Offset += size;
                Size += size;
                return new BitSequence(span);
            }
            else
            {
                Span<byte> firstSpan = _currentBuffer.Span[_tail.Offset..];
                firstSpan.Fill(255);
                _currentBuffer = GetBuffer(++_tail.Buffer);
                _tail.Offset = size - remaining;
                Size += size;
                Span<byte> secondSpan = _currentBuffer.Span.Slice(0, _tail.Offset);
                secondSpan.Fill(255);
                return new BitSequence(firstSpan, secondSpan);
            }
        }

        /// <summary>Writes a byte.</summary>
        /// <param name="v">The byte to write.</param>
        internal void WriteByte(byte v)
        {
            Expand(1);
            _currentBuffer.Span[_tail.Offset] = v;
            _tail.Offset++;
            Size++;
        }

        /// <summary>Writes a span of bytes. The encoder capacity is expanded if required, the size and tail position
        /// are increased according to the span length.</summary>
        /// <param name="span">The data to write as a span of bytes.</param>
        internal void WriteByteSpan(ReadOnlySpan<byte> span)
        {
            int length = span.Length;
            if (length > 0)
            {
                Expand(length);
                Size += length;
                int offset = _tail.Offset;
                int remaining = _currentBuffer.Length - offset;
                Debug.Assert(remaining > 0); // guaranteed by Expand
                int sz = Math.Min(length, remaining);
                if (length > remaining)
                {
                    span.Slice(0, remaining).CopyTo(_currentBuffer.Span.Slice(offset, sz));
                }
                else
                {
                    span.CopyTo(_currentBuffer.Span.Slice(offset, length));
                }
                _tail.Offset += sz;
                length -= sz;

                if (length > 0)
                {
                    _currentBuffer = GetBuffer(++_tail.Buffer);
                    if (remaining == 0)
                    {
                        span.CopyTo(_currentBuffer.Span.Slice(0, length));
                    }
                    else
                    {
                        span.Slice(remaining, length).CopyTo(_currentBuffer.Span.Slice(0, length));
                    }
                    _tail.Offset = length;
                }
            }
        }

        private static int Distance(ReadOnlyMemory<ReadOnlyMemory<byte>> data, Position start, Position end)
        {
            // If both the start and end position are in the same array buffer just
            // compute the offsets distance.
            if (start.Buffer == end.Buffer)
            {
                return end.Offset - start.Offset;
            }

            // If start and end position are in different buffers we need to accumulate the
            // size from start offset to the end of the start buffer, the size of the intermediary
            // buffers, and the current offset into the last buffer.
            ReadOnlyMemory<byte> buffer = data.Span[start.Buffer];
            int size = buffer.Length - start.Offset;
            for (int i = start.Buffer + 1; i < end.Buffer; ++i)
            {
                checked
                {
                    size += data.Span[i].Length;
                }
            }
            checked
            {
                return size + end.Offset;
            }
        }

        /// <summary>Expands the encoder's buffer to make room for more data. If the bytes remaining in the buffer are
        /// not enough to hold the given number of bytes, allocates a new byte array. The caller should then consume the
        /// new bytes immediately; calling Expand repeatedly is not supported.</summary>
        /// <param name="n">The number of bytes to accommodate in the buffer.</param>
        private void Expand(int n)
        {
            Debug.Assert(n > 0);
            int remaining = Capacity - Size;
            if (n > remaining)
            {
                int size = Math.Max(DefaultBufferSize, _currentBuffer.Length * 2);
                size = Math.Max(n - remaining, size);
                byte[] buffer = new byte[size];

                if (_bufferVector.Length == 0)
                {
                    // First Expand for a new Ice encoder constructed with no buffer.
                    Debug.Assert(_currentBuffer.Length == 0);
                    _bufferVector = new ReadOnlyMemory<byte>[] { buffer };
                    _currentBuffer = buffer;
                }
                else
                {
                    var newBufferVector = new ReadOnlyMemory<byte>[_bufferVector.Length + 1];
                    _bufferVector.CopyTo(newBufferVector.AsMemory());
                    newBufferVector[^1] = buffer;
                    _bufferVector = newBufferVector;

                    if (remaining == 0)
                    {
                        // Patch _tail to point to the first byte in the new buffer.
                        Debug.Assert(_tail.Offset == _currentBuffer.Length);
                        _currentBuffer = buffer;
                        _tail.Buffer++;
                        _tail.Offset = 0;
                    }
                }
                Capacity += buffer.Length;
            }

            // Once Expand returns, _tail points to a writeable byte.
            Debug.Assert(_tail.Offset < _currentBuffer.Length);
        }

        /// <summary>Returns the buffer at the given index.</summary>
        private Memory<byte> GetBuffer(int index) => MemoryMarshal.AsMemory(_bufferVector.Span[index]);
    }
}
