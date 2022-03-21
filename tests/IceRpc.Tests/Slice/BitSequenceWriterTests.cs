// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Tests;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class BitSequenceWriterTests
{

    /// <summary>Provides test case data for
    /// <see cref="Write_bit_sequence_clears_memory(byte[], byte[], IList<Memory<byte>>?, int)"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> WriteClearsMemorySource
    {
        get
        {
            (byte[], byte[], IList<Memory<byte>>?)[] testData =
            {
                (
                    new byte[] { 1, 2, 3 },
                    Array.Empty<byte>(),
                    null
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    null
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    new Memory<byte>[] { new byte[] { 7, 8, 9 } }
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    new Memory<byte>[] { new byte[] { 7, 8, 9 }, new byte[] { 10, 11, 12 } }
                ),
            };
            foreach ((
                byte[] firstBytes,
                byte[] secondBytes,
                IList<Memory<byte>>? additionalMemory) in testData)
            {
                yield return new TestCaseData(firstBytes, secondBytes, additionalMemory);
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
        var additionalMemory = new Memory<byte>[]
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
            foreach (byte i in enumerator.Current)
            {
                Assert.That(i, Is.EqualTo(pattern));
            }
        }
    }

    /// <summary>Verifies that calling <see cref="BitSequenceWriter.Write"/> correctly zeros the provided spans
    /// and additional memory</summary>
    /// <param name="firstBytes">The bytes that will be used to create the first span.</param>
    /// <param name="secondBytes">The bytes that will be used to create the second span. (Can be empty)</param>
    /// <param name="additionalMemory">The list of memory used for additional memory. (Optional)</param>
    [Test, TestCaseSource(nameof(WriteClearsMemorySource))]
    public void Write_bit_sequence_clears_memory(
        byte[] firstBytes,
        byte[] secondBytes,
        IList<Memory<byte>>? additionalMemory)
    {
        int additionalMemSize = additionalMemory != null ? additionalMemory.Sum(m => m.Length) : 0;
        int size = (firstBytes.Length + secondBytes.Length + additionalMemSize) * 8;
        var writer = new BitSequenceWriter(new SpanEnumerator(firstBytes.AsSpan(), secondBytes.AsSpan(), additionalMemory));

        for (int i = 0; i < size; ++i)
        {
            writer.Write(false);
        }

        var enumerator = new SpanEnumerator(firstBytes.AsSpan(), secondBytes.AsSpan(), additionalMemory);
        while (enumerator.MoveNext())
        {
            Assert.That(enumerator.Current.ToArray().All(o => o == 0), Is.True);
        }
    }

    /// <summary> TODO </summary>
    [Test]
    public void Get_bit_sequence_writer_from_slice_encoder(
        [Values(1, 8, 17, 97, 791)] int bitSize,
        [Values(0x00, 0x01, 0x12, 0x3C, 0x55, 0xFF)] byte pattern)
    {
        // Arrange
        const int maxBufferSize = 7;
        using var testPool = new TestMemoryPool(maxBufferSize);
        var pipe = new Pipe(new PipeOptions(pool: testPool));
        var encoder = new SliceEncoder(pipe.Writer, Encoding.Slice20);
        BitSequenceWriter writer = encoder.GetBitSequenceWriter(bitSize);

        // Act
        for (int i = 0; i < bitSize; ++i)
        {
            writer.Write(IsSet(i, pattern));
        }
        pipe.Writer.Complete();
        bool read = pipe.Reader.TryRead(out ReadResult readResult);
        var reader = new BitSequenceReader(readResult.Buffer);

        // Assert
        Assert.That(read, Is.True);
        Assert.That(readResult.Buffer.IsSingleSegment, Is.EqualTo(bitSize <= maxBufferSize * 8));
        for (int i = 0; i < bitSize; ++i)
        {
            Assert.That(reader.Read(), Is.EqualTo(IsSet(i, pattern)));
        }

        // Cleanup
        pipe.Reader.Complete();
    }

    private static bool IsSet(int bitIndex, byte pattern) => (pattern & (1 << (bitIndex % 8))) != 0;
}
