// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IceRpc.Slice.Internal
{
    /// <summary>Implements a custom buffer writer using another buffer writer.</summary>
    internal class BufferWriter // TODO: implement IBufferWriter<byte>
    {
        /// <summary>Represents a position in the underlying buffer list. This position consists of the index of the
        /// buffer in the list and the offset into that buffer.</summary>
        internal record struct Position
        {
            /// <summary>The zero based index of the buffer.</summary>
            internal int Buffer;

            /// <summary>The offset into the buffer.</summary>
            internal int Offset;

            /// <summary>Creates a new position from the buffer and offset values.</summary>
            /// <param name="buffer">The zero based index of the buffer in the buffer list.</param>
            /// <param name="offset">The offset into the buffer.</param>
            internal Position(int buffer, int offset)
            {
                Buffer = buffer;
                Offset = offset;
            }
        }

        /// <summary>The number of bytes that the underlying buffer list can hold without further allocation.</summary>
        internal int Capacity { get; private set; }

        /// <summary>Determines the current size of the buffer. This corresponds to the number of bytes already written.
        /// </summary>
        /// <value>The current size.</value>
        internal int Size { get; private set; }

        /// <summary>Gets the position of the next write.</summary>
        internal Position Tail => _tail;

        // All buffers before the tail buffer are fully used.
        private readonly List<Memory<byte>> _bufferList;

        // The buffer currently used by write operations. The tail Position always points to this buffer, and the tail
        // offset indicates how much of the buffer has been used.
        private Memory<byte> _currentBuffer;

        // The position for the next write operation.
        private Position _tail;

        // The underlying buffer writer.
        private readonly IBufferWriter<byte>? _underlying;

        // Constructs a BufferWriter from another buffer writer.
        internal BufferWriter(IBufferWriter<byte> underlying)
        {
            _underlying = underlying;

            _currentBuffer = _underlying.GetMemory();
            _bufferList = new List<Memory<byte>> { _currentBuffer };
            Capacity = _currentBuffer.Length;
        }

        // Constructs a BufferWriter over a single buffer.
        internal BufferWriter(Memory<byte> buffer)
        {
            Debug.Assert(buffer.Length > 0);

            _currentBuffer = buffer;
            _bufferList = new List<Memory<byte>> { _currentBuffer };
            Capacity = _currentBuffer.Length;
        }

        /// <summary>Finishes-off the current buffer.</summary>
        /// <remarks>If you are using a single buffer, you must instead slice your buffer as follows:
        /// buffer = buffer[0..bufferWriter.Size].</remarks>
        internal void Complete() => _underlying!.Advance(_tail.Offset);

        /// <summary>Returns the distance in bytes from start position to the tail position.</summary>
        /// <param name="start">The start position from where to calculate distance to the tail position.</param>
        /// <returns>The distance in bytes from the start position to the tail position.</returns>
        internal int Distance(Position start)
        {
            Debug.Assert(Tail.Buffer > start.Buffer ||
                        (Tail.Buffer == start.Buffer && Tail.Offset >= start.Offset));

            return Distance(_bufferList, start, Tail);
        }

        /// <summary>Rewrites a single byte at a given position.</summary>
        /// <param name="v">The byte value to write.</param>
        /// <param name="pos">The position to write to.</param>
        internal void RewriteByte(byte v, Position pos)
        {
            Memory<byte> buffer = _bufferList[pos.Buffer];

            if (pos.Offset < buffer.Length)
            {
                buffer.Span[pos.Offset] = v;
            }
            else
            {
                // (segN, segN.Count) points to the same byte as (segN + 1, 0)
                Debug.Assert(pos.Offset == buffer.Length);
                buffer = _bufferList[pos.Buffer + 1];
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
            Memory<byte> buffer = _bufferList[pos.Buffer];

            int remaining = Math.Min(data.Length, buffer.Length - pos.Offset);
            if (remaining > 0)
            {
                data.Slice(0, remaining).CopyTo(buffer.Span.Slice(pos.Offset, remaining));
            }

            if (remaining < data.Length)
            {
                buffer = _bufferList[pos.Buffer + 1];
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
                _currentBuffer = _bufferList[++_tail.Buffer];
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
                    _currentBuffer = _bufferList[++_tail.Buffer];
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

        private static int Distance(List<Memory<byte>> data, Position start, Position end)
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
            Memory<byte> buffer = data[start.Buffer];
            int size = buffer.Length - start.Offset;
            for (int i = start.Buffer + 1; i < end.Buffer; ++i)
            {
                checked
                {
                    size += data[i].Length;
                }
            }
            checked
            {
                return size + end.Offset;
            }
        }

        /// <summary>Expands the buffer writer's buffer to make room for more data. If the bytes remaining in the buffer
        /// are not enough to hold the given number of bytes, allocates a Memory. The caller should then consume _all_
        /// the new bytes immediately; calling Expand repeatedly is not supported.</summary>
        /// <param name="n">The number of bytes to accommodate in the buffer.</param>
        private void Expand(int n)
        {
            Debug.Assert(n > 0);
            int remaining = Capacity - Size;
            if (n > remaining)
            {
                if (_underlying == null)
                {
                    throw new InvalidOperationException(
                        "cannot expand the buffers of a buffer writer over a fixed-size buffer");
                }

                // We promise to fill the current buffer completely
                _underlying.Advance(_currentBuffer.Length);

                // A single buffer must satisfy the expansion request.
                Memory<byte> buffer = _underlying.GetMemory(n - remaining);
                _bufferList.Add(buffer);

                if (remaining == 0)
                {
                    // Patch _tail to point to the first byte in the new buffer.
                    Debug.Assert(_tail.Offset == _currentBuffer.Length);
                    _currentBuffer = buffer;
                    _tail.Buffer++;
                    _tail.Offset = 0;
                }
                Capacity += buffer.Length;
            }

            // Once Expand returns, _tail points to a writeable byte.
            Debug.Assert(_tail.Offset < _currentBuffer.Length);
        }
    }
}
