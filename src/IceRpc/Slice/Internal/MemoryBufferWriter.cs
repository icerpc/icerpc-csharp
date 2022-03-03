// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;

namespace IceRpc.Slice.Internal
{
    /// <summary>Implements a buffer writer over a single Memory{byte}.</summary>
    internal class MemoryBufferWriter : IBufferWriter<byte>
    {
        /// <summary>Returns the written portion of the underlying buffer.</summary>
        internal Memory<byte> WrittenMemory => _initialBuffer[0.._written];

        private Memory<byte> _available;

        private readonly Memory<byte> _initialBuffer;

        private int _written;

        /// <inheritdoc/>
        public void Advance(int count)
        {
            if (count < 0)
            {
                throw new ArgumentException($"{nameof(count)} can't be negative", nameof(count));
            }
            if (count > _available.Length)
            {
                throw new InvalidOperationException($"can't advance past the end of the underlying buffer");
            }
            _written += count;
            _available = _initialBuffer[_written..];
        }

        /// <summary>Clears the data written to the underlying buffer.</summary>
        public void Clear()
        {
            _written = 0;
            _available = _initialBuffer;
        }

        /// <inheritdoc/>
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (sizeHint > _available.Length)
            {
                throw new ArgumentException(
                    @$"requested at least {sizeHint} bytes from {nameof(MemoryBufferWriter)} when only {
                        _available.Length} bytes are available",
                    nameof(sizeHint));
            }
            return _available;
        }

        /// <inheritdoc/>
        public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;

        /// <summary>Constructs a new memory buffer writer over a buffer.</summary>
        /// <param name="buffer">The underlying buffer.</param>
        internal MemoryBufferWriter(Memory<byte> buffer)
        {
            if (buffer.IsEmpty)
            {
                throw new ArgumentException(nameof(buffer), $"{nameof(buffer)} can't be empty");
            }
            _initialBuffer = buffer;
            _available = _initialBuffer;
        }
    }
}
