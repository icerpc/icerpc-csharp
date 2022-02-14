// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Tests.SliceInternal
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [TestFixture("1.1")]
    [TestFixture("2.0")]
    [Parallelizable(scope: ParallelScope.All)]
    public class SequenceTests
    {
        private readonly Memory<byte> _buffer;
        private readonly SliceEncoding _encoding;
        private readonly SingleBufferWriter _bufferWriter;

        public SequenceTests(string encoding)
        {
            _encoding = SliceEncoding.FromString(encoding);
            _buffer = new byte[1024 * 1024];
            _bufferWriter = new SingleBufferWriter(_buffer);
        }

        /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSpan"/> and
        /// <see cref="SliceDecoderExtensions.DecodeSequence"/> with a fixed-size numeric value type.</summary>
        /// <param name="size">An int used to specify how many elements to generate in the sequence.</param>
        [TestCase(0)]
        [TestCase(256)]
        public void Sequence_FixedSizeNumeric(int size)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);
            int[] p1 = Enumerable.Range(0, size).ToArray();

            encoder.EncodeSpan(new ReadOnlySpan<int>(p1));
            int[] r1 = decoder.DecodeSequence<int>();

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(decoder.Consumed, Is.EqualTo(_bufferWriter.WrittenBuffer.Length));
            Assert.That(decoder.Consumed, Is.EqualTo(encoder.GetSizeLength(size) + size * sizeof(int)));
        }

        /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence"/> and
        /// <see cref="SliceDecoderExtensions.DecodeSequence"/> with a reference type.
        /// Also tests <see cref="SliceDecoder.DecodeString"/>.</summary>
        /// <param name="size">An int used to specify how many elements to generate in the sequence.</param>
        [TestCase(0)]
        [TestCase(256)]
        public void Sequence_String(int size)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);
            IEnumerable<string> p1 = Enumerable.Range(0, size).Select(i => $"string-{i}");

            encoder.EncodeSequence(p1, (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));
            IEnumerable<string> r1 = decoder.DecodeSequence(
                minElementSize: 1,
                (ref SliceDecoder decoder) => decoder.DecodeString());

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(decoder.Consumed, Is.EqualTo(_bufferWriter.WrittenBuffer.Length));
        }

        /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence"/> and
        /// <see cref="SliceDecoderExtensions.DecodeSequence"/> with a value type. Includes testing
        /// the <see cref="T[]"/>, <see cref="ImmutableArray{T}"/>, and <see cref="ArraySegment{T}"/>
        /// cases for <see cref="SliceEncoderExtensions.EncodeSequence"/>. Finally, covers
        /// <see cref="SliceDecoder.DecodeLong"/></summary>
        /// <param name="size">An int used to specify how many elements to generate in the sequence.</param>
        [TestCase(0)]
        [TestCase(256)]
        public void Sequence_Long(int size)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);

            IEnumerable<long> p1 = Enumerable.Range(0, size).Select(i => (long)i);
            ImmutableArray<long> p2 = ImmutableArray.CreateRange(p1);
            ArraySegment<long> p3 = new ArraySegment<long>(p1.ToArray());
            long[] p4 = p1.ToArray();

            encoder.EncodeSequence(p1);
            encoder.EncodeSequence(p2);
            encoder.EncodeSequence(p3);
            encoder.EncodeSequence(p4);

            long[] r1 = decoder.DecodeSequence(minElementSize: 1, (ref SliceDecoder decoder) => decoder.DecodeLong());
            long[] r2 = decoder.DecodeSequence(minElementSize: 1, (ref SliceDecoder decoder) => decoder.DecodeLong());
            long[] r3 = decoder.DecodeSequence(minElementSize: 1, (ref SliceDecoder decoder) => decoder.DecodeLong());
            List<long> r4 = decoder.DecodeSequence(
                minElementSize: 1,
                (int i) => new List<long>(i),
                (ref SliceDecoder decoder) => decoder.DecodeLong());

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(p2, Is.EqualTo(r2));
            Assert.That(p3, Is.EqualTo(r3));
            Assert.That(p4, Is.EqualTo(r4));

            Assert.That(decoder.Consumed, Is.EqualTo(_bufferWriter.WrittenBuffer.Length));
        }
    }
}
