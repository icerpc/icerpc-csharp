// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;

namespace IceRpc.Slice.Internal
{
    /// <summary>Implements a buffer writer over a single Memory{byte}.</summary>
    internal class SingleBufferWriter : IBufferWriter<byte>
    {
        private Memory<byte> _available;

        private readonly Memory<byte> _initialBuffer;

        private int _written;

        /// <summary>Returns the written portion of the underlying buffer.</summary>
        internal Memory<byte> WrittenBuffer => _initialBuffer[0.._written];

        /// <inheritdoc/>
        public Memory<byte> GetMemory(int sizeHint)
        {
            if (sizeHint > _available.Length)
            {
                throw new ArgumentException(
                    @$"requested at least {sizeHint} bytes from {nameof(SingleBufferWriter)} when only {
                        _available.Length} bytes are available");
            }
            return _available;
        }

        /// <inheritdoc/>
        public Span<byte> GetSpan(int sizeHint) => GetMemory(sizeHint).Span;

        /// <inheritdoc/>
        public void Advance(int count)
        {
            _written += count;
            _available = _initialBuffer[_written..];
        }

        /// <summary>Constructs a new single buffer writer over a buffer.</summary>
        /// <param name="buffer">The underlying buffer.</param>
        internal SingleBufferWriter(Memory<byte> buffer)
        {
            _initialBuffer = buffer;
            _available = _initialBuffer;
        }
    }
}
