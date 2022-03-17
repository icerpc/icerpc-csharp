// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Tests;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class BitSequenceTests
{

    private static IEnumerable<TestCaseData> WriteClearsMemorySource
    {
        get
        {
            (byte[], byte[], IList<Memory<byte>>?, int writes)[] testData =
            {
                (
                    new byte[] { 1, 2, 3 },
                    Array.Empty<byte>(),
                    null,
                    1
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    null,
                    2
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    new Memory<byte>[]
                    {
                        new byte[] { 7, 8, 9 }
                    },
                    3
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    new Memory<byte>[]
                    {
                        new byte[] { 7, 8, 9 },
                        new byte[] { 10, 11, 12 }
                    },
                    4
                ),
            };
            foreach ((
                byte[] firstBytes,
                byte[] secondBytes,
                IList<Memory<byte>>? additionalMemory,
                int writes) in testData)
            {
                yield return new TestCaseData(firstBytes, secondBytes, additionalMemory, writes);
            }
        }
    }

     /// <summary>Verifies that calling <see cref="BitSequenceWriter.Write"/> correctly writes the specified
     /// bit sequence to the provided spans and memory.</summary>
     /// <param name="pattern">The byte pattern to write.</param>
    [TestCase(0)]
    [TestCase(0xFF)]
    [TestCase(0xAA)]
    [TestCase(0x5B)]
    public void Write_bit_sequence(byte pattern)
    {
        Span<byte> firstSpan = new byte[3];
        Span<byte> secondSpan = new byte[30];
        IList<Memory<byte>> additionalMemory = new Memory<byte>[]
        {
            new byte[40],
            new byte[60]
        };
        const int size = (3 + 30 + 40 + 60) * 8; // in bits
        var writer = new BitSequenceWriter(new SpanEnumerator(firstSpan, secondSpan, additionalMemory));

        // Writing the bit sequence patterns to first span, second span, and additional memory
        for (int i = 0; i < size; ++i)
        {
            writer.Write(IsSet(i, pattern));
        }

        // Enumerating through the written spans and memory to validate that the write was successful
        var enumerator = new SpanEnumerator(firstSpan, secondSpan, additionalMemory);
        while (enumerator.MoveNext())
        {
            foreach (byte i in enumerator.Current) {
                Assert.That(i, Is.EqualTo(pattern));
            }
        }
    }

    /// <summary>Verifies that calling <see cref="BitSequenceWriter.Write"/> correctly writes the specified
    /// bit sequence to the provided spans and memory.</summary>
    /// <param name="pattern">The byte pattern to write.</param>
    [Test, TestCaseSource(nameof(WriteClearsMemorySource))]
    public void Write_bit_seqeunce_clears_memory(
        byte[] firstBytes,
        byte[] secondBytes,
        IList<Memory<byte>>? additionalMemory,
        int writes)
    {
        var enumerator = new SpanEnumerator(firstBytes.AsSpan(), secondBytes.AsSpan(), additionalMemory.AsSpan());
        var writer = new BitSequenceWriter(enumerator);
        for (int i = 0; i < writes - 1; i++)
        {
            writer.Write(false);
        }

        writer.Write(false);

        Assert.That(enumerator.Current.ToArray().All(o => o == 0), Is.True);
    }

    [Test]
    public void BitSequence_GetBitSequenceWriter(
        [Values(1, 8, 17, 97, 791)] int bitSize,
        [Values(0x00, 0x01, 0x12, 0x3C, 0x55, 0xFF)] byte pattern)
    {
        const int maxBufferSize = 7;
        using var testPool = new TestMemoryPool(maxBufferSize);
        var pipe = new Pipe(new PipeOptions(pool: testPool));
        var encoder = new SliceEncoder(pipe.Writer, Encoding.Slice20);

        BitSequenceWriter writer = encoder.GetBitSequenceWriter(bitSize);

        for (int i = 0; i < bitSize; ++i)
        {
            writer.Write(IsSet(i, pattern));
        }
        pipe.Writer.Complete();

        // Read it back with a bit sequence reader

        bool read = pipe.Reader.TryRead(out ReadResult readResult);
        Assert.That(read, Is.True);
        Assert.That(readResult.Buffer.IsSingleSegment, Is.EqualTo(bitSize <= maxBufferSize * 8));

        var reader = new BitSequenceReader(readResult.Buffer);

        for (int i = 0; i < bitSize; ++i)
        {
            Assert.That(reader.Read(), Is.EqualTo(IsSet(i, pattern)));
        }
        pipe.Reader.Complete();
    }

    private static bool IsSet(int bitIndex, byte pattern) => (pattern & (1 << (bitIndex % 8))) != 0;
}
