// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace IceRpc.Tests.Common;

/// <summary>Implements a buffer writer over a single Memory{byte}.</summary>
public class MemoryBufferWriter : IBufferWriter<byte>
{
    /// <summary>Gets the written portion of the underlying buffer.</summary>
    public Memory<byte> WrittenMemory => _initialBuffer[0.._written];

    private Memory<byte> _available;

    private readonly Memory<byte> _initialBuffer;

    private int _written;

    /// <summary>Constructs a new memory buffer writer over a buffer.</summary>
    /// <param name="buffer">The underlying buffer.</param>
    public MemoryBufferWriter(Memory<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            throw new ArgumentException($"The {nameof(buffer)} cannot be empty.", nameof(buffer));
        }
        _initialBuffer = buffer;
        _available = _initialBuffer;
    }

    /// <inheritdoc/>
    public void Advance(int count)
    {
        if (count < 0)
        {
            throw new ArgumentException($"The {nameof(count)} cannot be negative.", nameof(count));
        }
        if (count > _available.Length)
        {
            throw new InvalidOperationException("Cannot advance past the end of the underlying buffer.");
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
                $"Requested {sizeHint} bytes from {nameof(MemoryBufferWriter)} but only {_available.Length} bytes are available.",
                nameof(sizeHint));
        }
        return _available;
    }

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;
}
