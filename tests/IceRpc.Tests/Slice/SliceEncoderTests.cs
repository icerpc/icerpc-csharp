// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Tests;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SliceEncoderTests
{

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
