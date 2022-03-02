// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class MemoryBufferWriterTests
{
    [Test]
    public void MemoryBufferWriter_Constructor()
    {
        var writer = new MemoryBufferWriter(new byte[10]);
        Assert.That(writer.WrittenMemory.Length, Is.EqualTo(0));
    }

    [Test]
    public void MemoryBufferWriter_InvalidConstructor()
    {
        Assert.Throws<ArgumentException>(() =>new MemoryBufferWriter(Array.Empty<byte>()));
    }

    [Test]
    public void MemoryBufferWriter_Clear()
    {
        var writer = new MemoryBufferWriter(new byte[10]);
        writer.GetMemory();
        writer.Advance(2);
        Assert.That(writer.WrittenMemory.Length, Is.EqualTo(2));
        writer.Clear();
        Assert.That(writer.WrittenMemory.Length, Is.EqualTo(0));
    }

    [Test]
    public void MemoryBufferWriter_Advance()
    {
        var writer = new MemoryBufferWriter(new byte[10]);
        writer.Advance(5);
        Assert.That(writer.WrittenMemory.Length, Is.EqualTo(5));
        writer.Advance(2);
        Assert.That(writer.WrittenMemory.Length, Is.EqualTo(7));
    }

    [Test]
    public void MemoryBufferWriter_InvalidAdvance()
    {
        var writer = new MemoryBufferWriter(new byte[10]);
        Assert.Throws<ArgumentException>(() => writer.Advance(11));
    }

    [Test]
    public void MemoryBufferWriter_GetMemory()
    {
        var writer = new MemoryBufferWriter(new byte[10]);
        Memory<byte> memory = writer.GetMemory();
        Assert.That(memory.Length, Is.EqualTo(10));
        writer.Advance(2);
        memory = writer.GetMemory();
        Assert.That(memory.Length, Is.EqualTo(8));
    }

    [Test]
    public void MemoryBufferWriter_GetMemorySizeHint()
    {
        var writer = new MemoryBufferWriter(new byte[10]);
        Memory<byte> memory = writer.GetMemory(2);
        Assert.That(memory.Length, Is.EqualTo(10));
        writer.Advance(2);
        memory = writer.GetMemory(2);
        Assert.That(memory.Length, Is.EqualTo(8));
    }

    [Test]
    public void MemoryBufferWriter_InvalidGetMemory()
    {
        var writer = new MemoryBufferWriter(new byte[10]);
        Assert.Throws<ArgumentException>(() => writer.GetMemory(15));
    }

    [Test]
    public void MemoryBufferWriter_GetSpan()
    {
        var writer = new MemoryBufferWriter(new byte[10]);
        Span<byte> span = writer.GetSpan();
        writer.Advance(4);
        span = writer.GetSpan();
        Assert.That(span.Length, Is.EqualTo(6));
    }
}
