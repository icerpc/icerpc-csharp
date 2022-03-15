// Copyright (c) ZeroC, Inc. All rights reserved.
using IceRpc.Slice.Internal;
using IceRpc.Tests;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SpanEnumeratorTests
{

    [Test]
    public void Move_next_enumerates_through_spans_and_additional_memory_in_order()
    {
        Span<byte> firstSpan = new byte[] { 1, 2, 3 };
        Span<byte> secondSpan = new byte[] { 4, 5, 6 };

        IList<Memory<byte>> additionalMemory = new Memory<byte>[]
        {
            new byte[] { 7, 8, 9 },
            new byte[] { 10, 11, 12 }
        };

        var enumerator = new SpanEnumerator(firstSpan, secondSpan, additionalMemory);

        Assert.That(enumerator.MoveNext(), Is.True);
        Assert.That(enumerator.Current.Length, Is.EqualTo(3));
        Assert.That(enumerator.Current[0], Is.EqualTo(1));

        Assert.That(enumerator.MoveNext(), Is.True);
        Assert.That(enumerator.Current.Length, Is.EqualTo(3));
        Assert.That(enumerator.Current[0], Is.EqualTo(4));

        Assert.That(enumerator.MoveNext(), Is.True);
        Assert.That(enumerator.Current.Length, Is.EqualTo(3));
        Assert.That(enumerator.Current[0], Is.EqualTo(7));

        Assert.That(enumerator.MoveNext(), Is.True);
        Assert.That(enumerator.Current.Length, Is.EqualTo(3));
        Assert.That(enumerator.Current[0], Is.EqualTo(10));

        Assert.That(enumerator.MoveNext(), Is.False);
    }

    [Test]
    public void Move_next_does_not_pass_empty_span()
    {
        Span<byte> firstSpan = new byte[] { 1, 2, 3 };
        Span<byte> secondSpan = default;
        IList<Memory<byte>> additionalMemory = new Memory<byte>[]
        {
            new byte[] { 4, 5, 6 },
            new byte[] { 7, 8, 9 },

        };
        var enumerator = new SpanEnumerator(firstSpan, secondSpan,  additionalMemory);

        Assert.That(enumerator.MoveNext(), Is.True);
        Assert.That(enumerator.MoveNext(), Is.False);
        Assert.That(enumerator.MoveNext(), Is.False);
        Assert.That(enumerator.MoveNext(), Is.False);
    }
}
