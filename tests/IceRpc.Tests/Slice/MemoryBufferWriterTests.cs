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
        var lengthAfterAdvance2 = writer.WrittenMemory.Length;
        writer.Clear();

        Assert.That(lengthAfterAdvance2, Is.EqualTo(2));
        Assert.That(writer.WrittenMemory.Length, Is.EqualTo(0));
    }

    [Test]
    public void MemoryBufferWriter_Advance()
    {
        var writer = new MemoryBufferWriter(new byte[10]);

        writer.Advance(5);
        var lengthAfterAdvance5 = writer.WrittenMemory.Length;
        writer.Advance(2);
        var lengthAfterAdvance2 = writer.WrittenMemory.Length;

        Assert.That(lengthAfterAdvance5, Is.EqualTo(5));
        Assert.That(lengthAfterAdvance2, Is.EqualTo(7));
    }

    [Test]
    public void MemoryBufferWriter_InvalidAdvance()
    {
        var writer = new MemoryBufferWriter(new byte[10]);

        Assert.Throws<InvalidOperationException>(() => writer.Advance(11));
        Assert.Throws<ArgumentException>(() => writer.Advance(-1));
    }

    [Test]
    public void MemoryBufferWriter_GetMemory()
    {
        var writer = new MemoryBufferWriter(new byte[10]);

        var initialMemoryLength = writer.GetMemory().Length;
        writer.Advance(2);
        var afterAdvanceMemoryLength = writer.GetMemory().Length;

        Assert.That(initialMemoryLength, Is.EqualTo(10));
        Assert.That(afterAdvanceMemoryLength, Is.EqualTo(8));
    }

    [Test]
    public void MemoryBufferWriter_GetMemorySizeHint()
    {
        var writer = new MemoryBufferWriter(new byte[10]);

        var initialMemoryLength = writer.GetMemory(2).Length;
        writer.Advance(2);
        var afterAdvanceMemoryLength = writer.GetMemory(2).Length;

        Assert.That(initialMemoryLength, Is.EqualTo(10));
        Assert.That(afterAdvanceMemoryLength, Is.EqualTo(8));
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
