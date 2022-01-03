// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Tests.SliceInternal
{
    [Parallelizable(scope: ParallelScope.All)]
    public class BitSequenceTests
    {
        [Test]
        public void BitSequence_SpanEnumerator()
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

        [TestCase(0)]
        [TestCase(0xFF)]
        [TestCase(0xAA)]
        [TestCase(0x5B)]
        public void BitSequence_Writer(byte pattern)
        {
            Span<byte> firstSpan = new byte[3];
            firstSpan.Fill(0xAA);
            Span<byte> secondSpan = new byte[30];
            secondSpan.Fill(0xAA);
            IList<Memory<byte>> additionalMemory = new Memory<byte>[]
            {
                new byte[40],
                new byte[60]
            };
            additionalMemory[0].Span.Fill(0xAA);
            additionalMemory[1].Span.Fill(0xAA);

            const int size = (3 + 30 + 40 + 60) * 8; // in bits

            var writer = new BitSequenceWriter(new SpanEnumerator(firstSpan, secondSpan, additionalMemory));

            for (int i = 0; i < size; ++i)
            {
                writer.Write(IsSet(i, pattern));
            }

            // Verify we correctly wrote the pattern
            var enumerator = new SpanEnumerator(firstSpan, secondSpan, additionalMemory);
            while (enumerator.MoveNext())
            {
                for (int i = 0; i < enumerator.Current.Length; ++i)
                {
                    Assert.That(enumerator.Current[i], Is.EqualTo(pattern));
                }
            }
        }

        [Test]
        public void BitSequence_GetBitSequenceWriter(
            [Values(1, 8, 17, 97, 791)] int bitSize,
            [Values(0x00, 0x01, 0x12, 0x3C, 0x55, 0xFF)] byte pattern)
        {
            const int maxBufferSize = 7;
            using var testPool = new TestMemoryPool(maxBufferSize);
            var pipe = new Pipe(new PipeOptions(pool: testPool));
            var encoder = new IceEncoder(pipe.Writer, Encoding.Ice20);

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
}
