// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.Tests.SliceInternal
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [TestFixture("1.1")]
    [TestFixture("2.0")]
    [Parallelizable(scope: ParallelScope.All)]
    public class BuiltInTypesSequencesTests
    {
        private readonly Memory<byte> _buffer;
        private readonly IceEncoding _encoding;
        private readonly SingleBufferWriter _bufferWriter;

        public BuiltInTypesSequencesTests(string encoding)
        {
            _encoding = IceEncoding.FromString(encoding);
            _buffer = new byte[1024 * 1024];
            _bufferWriter = new SingleBufferWriter(_buffer);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences(int size)
        {
            var encoder = new IceEncoder(_bufferWriter, _encoding);
            var decoder = new IceDecoder(_buffer, _encoding);
            int[] p1 = Enumerable.Range(0, size).ToArray();
            encoder.EncodeSpan(new ReadOnlySpan<int>(p1));
            int[] r1 = decoder.DecodeSequence<int>();

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(decoder.Consumed, Is.EqualTo(_bufferWriter.WrittenBuffer.Length));
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Optional(int size)
        {
            var encoder = new IceEncoder(_bufferWriter, _encoding);
            var decoder = new IceDecoder(_buffer, _encoding);
            int[] p1 = Enumerable.Range(0, size).ToArray();
            encoder.EncodeSequenceWithBitSequence(p1, (ref IceEncoder encoder, int v) => encoder.EncodeInt(v));
            int[] r1 = decoder.DecodeSequenceWithBitSequence((ref IceDecoder decoder) => decoder.DecodeInt());

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(decoder.Consumed, Is.EqualTo(_bufferWriter.WrittenBuffer.Length));
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_String(int size)
        {
            var encoder = new IceEncoder(_bufferWriter, _encoding);
            var decoder = new IceDecoder(_buffer, _encoding);
            IEnumerable<string> p1 = Enumerable.Range(0, size).Select(i => $"string-{i}");
            encoder.EncodeSequence(
                p1,
                (ref IceEncoder encoder, string value) => encoder.EncodeString(value));
            IEnumerable<string> r1 = decoder.DecodeSequence(1, (ref IceDecoder decoder) => decoder.DecodeString());

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(decoder.Consumed, Is.EqualTo(_bufferWriter.WrittenBuffer.Length));
        }
    }
}
