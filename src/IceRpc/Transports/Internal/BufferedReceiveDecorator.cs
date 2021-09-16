// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    /// <summary>The BufferedReceiveDecorator is a single-stream connection decorator to provide buffered data
    /// receive. This helps to limit the number of operating system Receive calls when the user needs to read
    /// only a few bytes before reading more (typically to read a frame header) by receiving the data in a
    /// small buffer. It's similar to the C# System.IO.BufferedStream class. It's used by <see
    /// cref="SlicConnection"/>.</summary>
    internal class BufferedReceiveDecorator : ISingleStreamConnection
    {
        private ArraySegment<byte> _buffer;
        private readonly ISingleStreamConnection _decoratee;

        public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = 0;
            if (_buffer.Count > 0)
            {
                // If there's still data buffered for the payload, consume the buffered data.
                int length = Math.Min(_buffer.Count, buffer.Length);
                _buffer.Slice(0, length).AsMemory().CopyTo(buffer);
                _buffer = _buffer.Slice(length);
                received = length;
            }

            // Then, read the reminder from the underlying transport.
            if (received < buffer.Length)
            {
                received += await _decoratee.ReceiveAsync(buffer[received..], cancel).ConfigureAwait(false);
            }
            return received;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel) =>
            _decoratee.SendAsync(buffer, cancel);

        public ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel) =>
            _decoratee.SendAsync(buffers, cancel);

        internal BufferedReceiveDecorator(ISingleStreamConnection decoratee, int bufferSize = 256)
        {
            _decoratee = decoratee;

            // The _buffer data member holds the buffered data. There's no buffered data until we receive data
            // from the underlying connection so the array segment point to an empty segment.
            _buffer = new ArraySegment<byte>(new byte[bufferSize], 0, 0);
        }

        /// <inheritdoc/>
        internal async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(int byteCount, CancellationToken cancel = default)
        {
            if (byteCount > _buffer.Array!.Length)
            {
                throw new ArgumentOutOfRangeException(
                    $"{nameof(byteCount)} should be inferior to the buffer size of {_buffer.Array.Length} bytes");
            }

            // Receive additional data if there's not enough or no buffered data.
            if (_buffer.Count < byteCount || (byteCount == 0 && _buffer.Count == 0))
            {
                await ReceiveInBufferAsync(byteCount, cancel).ConfigureAwait(false);
                Debug.Assert(_buffer.Count >= byteCount);
            }

            if (byteCount == 0)
            {
                // Return all the buffered data.
                byteCount = _buffer.Count;
            }

            ReadOnlyMemory<byte> buffer = _buffer.Slice(0, byteCount);
            _buffer = _buffer.Slice(byteCount); // Remaining buffered data.
            return buffer;
        }

        internal void Rewind(int bytes)
        {
            if (bytes > _buffer.Offset)
            {
                throw new ArgumentOutOfRangeException($"{nameof(bytes)} is too large");
            }

            _buffer = new ArraySegment<byte>(_buffer.Array!, _buffer.Offset - bytes, _buffer.Count + bytes);
        }

        private async ValueTask ReceiveInBufferAsync(int byteCount, CancellationToken cancel = default)
        {
            Debug.Assert(byteCount == 0 || _buffer.Count < byteCount);

            int offset = _buffer.Count;

            // If there's not enough data buffered for byteCount we need to receive additional data. We first need
            // to make sure there's enough space in the buffer to read it however.
            if (_buffer.Count == 0)
            {
                // Use the full buffer array if there's no more buffered data.
                _buffer = new ArraySegment<byte>(_buffer.Array!);
            }
            else if (_buffer.Offset + _buffer.Count + byteCount > _buffer.Array!.Length)
            {
                // There's still buffered data but not enough space left in the array to read the given bytes.
                // In theory, the number of bytes to read should always be lower than the un-used buffer space
                // at the start of the buffer. We move the data at the end of the buffer to the beginning to
                // make space to read the given number of bytes.
                _buffer.CopyTo(_buffer.Array!, 0);
                _buffer = new ArraySegment<byte>(_buffer.Array);
            }
            else
            {
                // There's still buffered data and enough space to read the given bytes after the buffered data.
                _buffer = new ArraySegment<byte>(
                    _buffer.Array,
                    _buffer.Offset,
                    _buffer.Array.Length - _buffer.Offset);
            }

            // Receive additional data.
            if (byteCount == 0)
            {
                // Perform a single receive and we're done.
                offset += await _decoratee.ReceiveAsync(_buffer.Slice(offset), cancel).ConfigureAwait(false);
            }
            else
            {
                // Receive data until we have read at least "byteCount" bytes in the buffer.
                while (offset < byteCount)
                {
                    offset += await _decoratee.ReceiveAsync(_buffer.Slice(offset), cancel).ConfigureAwait(false);
                }
            }

            // Set _buffer to the buffered data array segment.
            _buffer = _buffer.Slice(0, offset);
        }
    }
}
