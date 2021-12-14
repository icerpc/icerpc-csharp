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

        internal Memory<byte> WrittenBuffer => _initialBuffer[0.._written];

        /// <inheritdoc/>
        public Memory<byte> GetMemory(int sizeHint)
        {
            if (sizeHint > _available.Length)
            {
                throw new ArgumentException(
                    @$"requested {sizeHint} bytes from SingleMemoryBuffer when only {
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

        internal SingleBufferWriter(Memory<byte> buffer)
        {
            _initialBuffer = buffer;
            _available = _initialBuffer;
        }
    }
}
