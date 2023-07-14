// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Collections.Concurrent;

namespace ZeroC.Slice.Tests;

/// <summary>A memory pool with "poisoned" memory and small buffers.</summary>
public class TestMemoryPool : MemoryPool<byte>
{
    /// <inheritdoc/>
    public override int MaxBufferSize { get; }

    private readonly ConcurrentStack<Memory<byte>> _stack = new();

    /// <inheritdoc/>
    public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
    {
        if (minBufferSize > MaxBufferSize)
        {
            throw new InvalidOperationException(
                $"minBufferSize {minBufferSize} is greater than the pool's MaxBufferSize");
        }

        return new MemoryOwner(this);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        // no-op
    }

    /// <summary>Construct a new test memory pool.</summary>
    /// <param name="maxBufferSize">The max buffer size.</param>
    public TestMemoryPool(int maxBufferSize) => MaxBufferSize = maxBufferSize;

    private sealed class MemoryOwner : IMemoryOwner<byte>
    {
        public Memory<byte> Memory { get; }

        private TestMemoryPool? _pool;

        public void Dispose()
        {
            if (_pool is not null)
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
