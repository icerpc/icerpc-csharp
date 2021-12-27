// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;

namespace IceRpc.Internal
{
    /// <summary>The buffered receiver class receives data from a byte source function into a buffer. The
    /// buffered data can be decoded into different Ice 2.0 types (byte, size and varulong) or be consumed as
    /// bytes. This class is useful to efficiently read Slic and Ice2 headers that require decoding data
    /// without necessarily knowing in advance how many bytes to read from the source.</summary>
    internal class BufferedReceiver : IDisposable
    {
        private readonly ReadOnlyMemory<byte> _buffer;
        private int _bufferOffset;
        private int _bufferLimitOffset;
        private readonly IMemoryOwner<byte>? _bufferOwner;
        private readonly Func<Memory<byte>, CancellationToken, ValueTask<int>>? _source;

        public void Dispose() => _bufferOwner?.Dispose();

        /// <summary>Constructs a new buffered receiver.</summary>
        /// <param name="source">The data source function to receive additional data.</param>
        /// <param name="bufferSize">The size of the buffer.</param>
        internal BufferedReceiver(
            Func<Memory<byte>, CancellationToken, ValueTask<int>> source,
            int bufferSize)
        {
            _source = source;
            _bufferOwner = MemoryPool<byte>.Shared.Rent(bufferSize);
            _buffer = _bufferOwner.Memory;
            _bufferOffset = 0;
            _bufferLimitOffset = 0;
        }

        /// <summary>Constructs a new buffer receiver.</summary>
        /// <param name="buffer">The buffer to read the data from.</param>
        internal BufferedReceiver(ReadOnlyMemory<byte> buffer)
        {
            _buffer = buffer;
            _bufferOffset = 0;
            _bufferLimitOffset = buffer.Length;
            _bufferOwner = null;
            _source = null;
        }

        /// <summary>Receives data from the source into the given buffer. This method only returns once the
        /// buffer is full.</summary>
        internal async ValueTask ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int receivedFromBuffer = 0;
            int remaining = _bufferLimitOffset - _bufferOffset;
            if (remaining > 0)
            {
                if (remaining > buffer.Length)
                {
                    _buffer[_bufferOffset..(_bufferOffset + buffer.Length)].CopyTo(buffer);
                    receivedFromBuffer = buffer.Length;
                    _bufferOffset += buffer.Length;
                }
                else
                {
                    _buffer[_bufferOffset.._bufferLimitOffset].CopyTo(buffer);
                    receivedFromBuffer = remaining;
                    _bufferOffset = _bufferLimitOffset = 0;
                }
            }

            if (receivedFromBuffer != buffer.Length)
            {
                if (_source == null)
                {
                    throw new InvalidOperationException("can't receive additional data");
                }

                for (int offset = receivedFromBuffer; offset != buffer.Length;)
                {
                    offset += await _source(buffer[offset..], cancel).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Receives a single byte from the source.</summary>
        internal async ValueTask<byte> ReceiveByteAsync(CancellationToken cancel)
        {
            if (_bufferOffset == _bufferLimitOffset)
            {
                await ReceiveMoreAsync(1, cancel).ConfigureAwait(false);
            }
            byte value = _buffer.Span[_bufferOffset];
            ++_bufferOffset;
            return value;
        }

        /// <summary>Receives an Ice 2.0 encoded size from the source.</summary>
        internal async ValueTask<int> ReceiveSizeAsync(CancellationToken cancel)
        {
            int remaining = _bufferLimitOffset - _bufferOffset;
            if (remaining == 0)
            {
                // We need at least one byte to read the size.
                await ReceiveMoreAsync(1, cancel).ConfigureAwait(false);
                remaining = _bufferLimitOffset - _bufferOffset;
            }

            int sizeLength = Ice20Encoding.DecodeSizeLength(_buffer.Span[_bufferOffset]);
            if (remaining < sizeLength)
            {
                // Read more data if there's not enough data in the buffer to decode the size.
                await ReceiveMoreAsync(sizeLength, cancel).ConfigureAwait(false);
            }

            int size = Ice20Encoding.DecodeSize(_buffer.Span[_bufferOffset..]).Size;
            _bufferOffset += sizeLength;
            return size;
        }

        /// <summary>Receives an Ice 2.0 encoded varulong from the source.</summary>
        internal async ValueTask<(ulong Value, int ValueLength)> ReceiveVarULongAsync(CancellationToken cancel)
        {
            int remaining = _bufferLimitOffset - _bufferOffset;
            if (remaining == 0)
            {
                // We need at least one byte to read the varulong.
                await ReceiveMoreAsync(1, cancel).ConfigureAwait(false);
                remaining = _bufferLimitOffset - _bufferOffset;
            }

            int valueLength = IceDecoder.DecodeVarLongLength(_buffer.Span[_bufferOffset]);
            if (remaining < valueLength)
            {
                // Read more data if there's not enough data in the buffer to decode the varulong.
                await ReceiveMoreAsync(valueLength, cancel).ConfigureAwait(false);
            }

            ulong value = IceDecoder.DecodeVarULong(_buffer.Span[_bufferOffset..]).Value;
            _bufferOffset += valueLength;
            return (value, valueLength);
        }

        private async ValueTask ReceiveMoreAsync(int length, CancellationToken cancel)
        {
            if (_bufferOwner == null || _source == null)
            {
                throw new InvalidOperationException("can't receive additional data");
            }

            // Receives additional data into the buffer from the source.
            int remaining = _bufferLimitOffset - _bufferOffset;
            if (length <= remaining)
            {
                throw new InvalidOperationException("enough buffered data, no need to receive more");
            }

            if (remaining > 0)
            {
                // There's still buffered data, move the buffered data at the start of the memory buffer and
                // read the additional data.
                _buffer[_bufferOffset.._bufferLimitOffset].CopyTo(_bufferOwner!.Memory);
                _bufferOffset = 0;
                _bufferLimitOffset = remaining;
            }
            else
            {
                _bufferOffset = 0;
                _bufferLimitOffset = 0;
            }

            while ((_bufferLimitOffset - _bufferOffset) < length)
            {
                int received = await _source!(_bufferOwner!.Memory[_bufferLimitOffset..], cancel).ConfigureAwait(false);
                if (received == 0)
                {
                    throw new InvalidDataException($"received 0 bytes where {length} bytes were expected");
                }
                _bufferLimitOffset += received;
            }
        }
    }
}
