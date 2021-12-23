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
        public void BuiltInTypesSequences_Bool(int size)
        {
            var encoder = new IceEncoder(_bufferWriter, _encoding);
            var decoder = new IceDecoder(_buffer, _encoding);
            bool[] p1 = Enumerable.Range(0, size).Select(i => i % 2 == 0).ToArray();
            encoder.EncodeArray(p1);
            bool[] r1 = decoder.DecodeSequence<bool>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(decoder.Consumed, _bufferWriter.WrittenBuffer.Length);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Byte(int size)
        {
            var encoder = new IceEncoder(_bufferWriter, _encoding);
            var decoder = new IceDecoder(_buffer, _encoding);
            byte[] p1 = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
            encoder.EncodeArray(p1);
            byte[] r1 = decoder.DecodeSequence<byte>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(decoder.Consumed, _bufferWriter.WrittenBuffer.Length);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Short(int size)
        {
            var encoder = new IceEncoder(_bufferWriter, _encoding);
            var decoder = new IceDecoder(_buffer, _encoding);
            short[] p1 = Enumerable.Range(0, size).Select(i => (short)i).ToArray();
            encoder.EncodeArray(p1);
            short[] r1 = decoder.DecodeSequence<short>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(decoder.Consumed, _bufferWriter.WrittenBuffer.Length);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Int(int size)
        {
            var encoder = new IceEncoder(_bufferWriter, _encoding);
            var decoder = new IceDecoder(_buffer, _encoding);
            int[] p1 = Enumerable.Range(0, size).ToArray();
            encoder.EncodeArray(p1);
            int[] r1 = decoder.DecodeSequence<int>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(decoder.Consumed, _bufferWriter.WrittenBuffer.Length);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Long(int size)
        {
            var encoder = new IceEncoder(_bufferWriter, _encoding);
            var decoder = new IceDecoder(_buffer, _encoding);
            long[] p1 = Enumerable.Range(0, size).Select(i => (long)i).ToArray();
            encoder.EncodeArray(p1);
            long[] r1 = decoder.DecodeSequence<long>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(decoder.Consumed, _bufferWriter.WrittenBuffer.Length);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Float(int size)
        {
            var encoder = new IceEncoder(_bufferWriter, _encoding);
            var decoder = new IceDecoder(_buffer, _encoding);
            float[] p1 = Enumerable.Range(0, size).Select(i => (float)i).ToArray();
            encoder.EncodeArray(p1);
            float[] r1 = decoder.DecodeSequence<float>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(decoder.Consumed, _bufferWriter.WrittenBuffer.Length);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Double(int size)
        {
            var encoder = new IceEncoder(_bufferWriter, _encoding);
            var decoder = new IceDecoder(_buffer, _encoding);
            double[] p1 = Enumerable.Range(0, size).Select(i => (double)i).ToArray();
            encoder.EncodeArray(p1);
            double[] r1 = decoder.DecodeSequence<double>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(decoder.Consumed, _bufferWriter.WrittenBuffer.Length);
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

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(decoder.Consumed, _bufferWriter.WrittenBuffer.Length);
        }
    }
}
