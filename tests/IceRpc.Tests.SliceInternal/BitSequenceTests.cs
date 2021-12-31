// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Buffers;

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
                writer.Write((pattern & (1 << (i % 8))) != 0);
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
    }
}
