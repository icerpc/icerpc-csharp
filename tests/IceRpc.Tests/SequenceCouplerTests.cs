// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests;

[SingleThreaded]
[FixtureLifeCycle(LifeCycle.SingleInstance)]
public class SequenceCouplerTests
{
    private static IEnumerable<TestCaseData> ConnectSequencesSources
    {
        get
        {
            var array1 = new byte[] { 1, 2, 3 };
            var array2 = new byte[] { 3, 2, 1 };

            // A custom memory pool with a tiny max buffer size
            using var customPool = new TestMemoryPool(7);
            var pipe = new Pipe(new PipeOptions(pool: customPool, minimumSegmentSize: 5));

            for (int i = 0; i < 10; ++i)
            {
                Memory<byte> memory = pipe.Writer.GetMemory();
                memory.Span.Fill((byte)i);
                pipe.Writer.Advance(memory.Length);
            }
            pipe.Writer.Complete();
            pipe.Reader.TryRead(out ReadResult readResult);

            yield return new TestCaseData(readResult.Buffer, readResult.Buffer);
            yield return new TestCaseData(new ReadOnlySequence<byte>(array1), readResult.Buffer);
            yield return new TestCaseData(ReadOnlySequence<byte>.Empty, ReadOnlySequence<byte>.Empty);
            yield return new TestCaseData(ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(array1));
            yield return new TestCaseData(new ReadOnlySequence<byte>(array1), ReadOnlySequence<byte>.Empty);
            yield return new TestCaseData(new ReadOnlySequence<byte>(array1), new ReadOnlySequence<byte>(array2));
        }
    }

    // We use the same SequenceCoupler instance over and over as it's the typical way to use this class.
    private readonly SequenceCoupler _sut = new();

    /// <summary>Verifies that <see cref="SequenceCoupler.Connect" /> correctly connects two sequences.</summary>
    [Test, TestCaseSource(nameof(ConnectSequencesSources))]
    public void Connect_sequences(ReadOnlySequence<byte> first, ReadOnlySequence<byte> second)
    {
        ReadOnlySequence<byte> connected = _sut.Connect(first, second);

        Assert.That(connected.Length, Is.EqualTo(first.Length + second.Length));

        ReadOnlySequence<byte>.Enumerator enumerator = connected.GetEnumerator();

        if (!first.IsEmpty)
        {
            foreach (ReadOnlyMemory<byte> memory in first)
            {
                enumerator.MoveNext();
                Assert.That(memory, Is.EqualTo(enumerator.Current));
            }
        }

        if (!second.IsEmpty)
        {
            foreach (ReadOnlyMemory<byte> memory in second)
            {
                enumerator.MoveNext();
                Assert.That(memory, Is.EqualTo(enumerator.Current));
            }
        }
    }
}
