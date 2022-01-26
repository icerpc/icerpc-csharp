// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Collections.Concurrent;

namespace IceRpc.Tests
{
    /// <summary>A memory pool with "poisoned" memory and small buffers.</summary>
    public class TestMemoryPool : MemoryPool<byte>
    {
        public override int MaxBufferSize { get; }

        private readonly ConcurrentStack<Memory<byte>> _stack = new();

        public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
        {
            if (minBufferSize > MaxBufferSize)
            {
                throw new InvalidOperationException(
                    $"minBufferSize {minBufferSize} is greater than the pool's MaxBufferSize");
            }

            return new MemoryOwner(this);
        }

        protected override void Dispose(bool disposing)
        {
            // no-op
        }

        public TestMemoryPool(int maxBufferSize) => MaxBufferSize = maxBufferSize;

        private class MemoryOwner : IMemoryOwner<byte>
        {
            public Memory<byte> Memory { get; }

            private TestMemoryPool? _pool;

            public void Dispose()
            {
                if (_pool != null)
                {
                    _pool._stack.Push(Memory);
                    _pool = null;
                }
            }

            internal MemoryOwner(TestMemoryPool pool)
            {
                _pool = pool;

                if (_pool._stack.TryPop(out Memory<byte> buffer))
                {
                    Memory = buffer;
                }
                else
                {
                    Memory = new byte[_pool.MaxBufferSize];
                }

                // "poison" memory
                Memory.Span.Fill(0xAA);
            }
        }
    }
}
