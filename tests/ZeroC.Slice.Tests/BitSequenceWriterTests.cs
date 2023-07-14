// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.IO.Pipelines;
using ZeroC.Slice.Internal;

namespace ZeroC.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class BitSequenceWriterTests
{
    /// <summary>Provides test case data for <see cref="Write_fails(byte[], byte[], IList{Memory{byte}}?)" /> test.
    /// </summary>
    private static IEnumerable<TestCaseData> WriteFailsDataSource
    {
        get
        {
            yield return new TestCaseData(new byte[] { 1, 2, 3 }, Array.Empty<byte>(), null);
            yield return new TestCaseData(new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6 }, null);
            yield return new TestCaseData(
                new byte[] { 1, 2, 3 },
                new byte[] { 4, 5, 6 },
                new Memory<byte>[] { new byte[] { 7, 8, 9 } });
            yield return new TestCaseData(
                new byte[] { 1, 2, 3 },
                new byte[] { 4, 5, 6 },
                new Memory<byte>[] { new byte[] { 7, 8, 9 }, new byte[] { 10, 11, 12 } });
        }
    }

    /// <summary>Provides test case data for
    /// <see cref="Write_bit_sequence_clears_memory(byte[], byte[], IList{Memory{byte}}?, int)" /> test.</summary>
    private static IEnumerable<TestCaseData> WriteClearsDataSource
    {
        get
        {
            yield return new TestCaseData(new byte[] { 1, 2, 3 }, Array.Empty<byte>(), null, 0);
            yield return new TestCaseData(new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6 }, null, 3);
            yield return new TestCaseData(
                new byte[] { 1, 2, 3 },
                new byte[] { 4, 5, 6 },
                new Memory<byte>[] { new byte[] { 7, 8, 9 } },
                6);
            yield return new TestCaseData(
                new byte[] { 1, 2, 3 },
                new byte[] { 4, 5, 6 },
                new Memory<byte>[] { new byte[] { 7, 8, 9 }, new byte[] { 10, 11, 12 } },
                9);
        }
    }

    [Test]
    public void Get_bit_sequence_writer_from_slice_encoder(
        [Values(1, 8, 17, 97, 791)] int bitSize,
        [Values(0x00, 0x01, 0x12, 0x3C, 0x55, 0xFF)] byte pattern)
    {
        // Arrange
        const int maxBufferSize = 7;
        using var testPool = new TestMemoryPool(maxBufferSize);
        var pipe = new Pipe(new PipeOptions(pool: testPool));
        var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
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

    /// <summary>Verifies that calling <see cref="BitSequenceWriter.Write" /> correctly writes the specified bit
    /// sequence to the provided spans and memory.</summary>
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
        var writer = new BitSequenceWriter(firstSpan, secondSpan, additionalMemory);

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

    /// <summary>Verifies that calling <see cref="BitSequenceWriter.Write" /> correctly zeros the provided spans
    /// and additional memory.</summary>
    /// <param name="firstBytes">The bytes that will be used to create the first span.</param>
    /// <param name="secondBytes">The bytes that will be used to create the second span. (Can be empty)</param>
    /// <param name="additionalMemory">The list of memory used for additional memory. (Optional)</param>
    /// <param name="writes">The number of writes to make to move the BitSequenceWriter to the final span.</param>
    [Test, TestCaseSource(nameof(WriteClearsDataSource))]
    public void Write_bit_sequence_clears_memory(
        byte[] firstBytes,
        byte[] secondBytes,
        IList<Memory<byte>>? additionalMemory,
        int writes)
    {
        // Arrange
        var writer = new BitSequenceWriter(firstBytes.AsSpan(), secondBytes.AsSpan(), additionalMemory);
        for (int i = 0; i < writes * 8; ++i)
        {
            writer.Write(true);
        }

        // Act
        writer.Write(false);

        // Assert
        var enumerator = new SpanEnumerator(firstBytes.AsSpan(), secondBytes.AsSpan(), additionalMemory);
        Span<byte> current = default;
        while (enumerator.MoveNext())
        {
            current = enumerator.Current;
        }
        Assert.That(current.ToArray().All(o => o == 0), Is.True);
    }

    /// <summary>Verifies that calling <see cref="BitSequenceWriter.Write" /> on a BitSequenceWriter that has already
    /// enumerated fully through its spans throws an invalid operation exception.</summary>
    /// <param name="firstBytes">The bytes that will be used to create the first span.</param>
    /// <param name="secondBytes">The bytes that will be used to create the second span. (Can be empty)</param>
    /// <param name="additionalMemory">The list of memory used for additional memory. (Optional)</param>
    [Test, TestCaseSource(nameof(WriteFailsDataSource))]
    public void Write_fails(
        byte[] firstBytes,
        byte[] secondBytes,
        IList<Memory<byte>>? additionalMemory)
    {
        // We cannot follow traditional AAA with this test as the writer must be accessible inside of the lambda
        // expression. Thus the arrange and act sections were moved into the NUnit assertion.
        Assert.That(() =>
        {
            // Arrange
            int additionalMemSize = additionalMemory?.Sum(m => m.Length) ?? 0;
            int size = (firstBytes.Length + secondBytes.Length + additionalMemSize) * 8;
            var writer = new BitSequenceWriter(firstBytes, secondBytes, additionalMemory);
            for (int i = 0; i < size; ++i)
            {
                writer.Write(true);
            }

            // Act
            writer.Write(false);

        }, Throws.InvalidOperationException);
    }

    private static bool IsSet(int bitIndex, byte pattern) => (pattern & (1 << (bitIndex % 8))) != 0;
}
