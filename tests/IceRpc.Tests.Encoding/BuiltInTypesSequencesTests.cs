// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace IceRpc.Tests.Encoding
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [TestFixture((byte)1, (byte)1)]
    [TestFixture((byte)2, (byte)0)]
    [Parallelizable(scope: ParallelScope.All)]
    public class BuiltInTypesSequencesTests
    {
        private readonly IceRpc.Encoding _encoding;
        private readonly byte[] _buffer;
        private readonly IceEncoder _encoder;
        private readonly IceDecoder _decoder;

        public BuiltInTypesSequencesTests(byte encodingMajor, byte encodingMinor)
        {
            _encoding = IceRpc.Encoding.FromMajorMinor(encodingMajor, encodingMinor); // TODO
            _buffer = new byte[1024 * 1024];
            _encoder = new IceEncoder(_encoding, _buffer);
            _decoder = new IceDecoder(_buffer, _encoding);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Bool(int size)
        {
            bool[] p1 = Enumerable.Range(0, size).Select(i => i % 2 == 0).ToArray();
            _encoder.EncodeArray(p1);
            bool[] r1 = _decoder.DecodeArray<bool>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_decoder.Pos, _encoder.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Byte(int size)
        {
            byte[] p1 = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
            _encoder.EncodeArray(p1);
            byte[] r1 = _decoder.DecodeArray<byte>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_decoder.Pos, _encoder.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Short(int size)
        {
            short[] p1 = Enumerable.Range(0, size).Select(i => (short)i).ToArray();
            _encoder.EncodeArray(p1);
            short[] r1 = _decoder.DecodeArray<short>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_decoder.Pos, _encoder.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Int(int size)
        {
            int[] p1 = Enumerable.Range(0, size).ToArray();
            _encoder.EncodeArray(p1);
            int[] r1 = _decoder.DecodeArray<int>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_decoder.Pos, _encoder.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Long(int size)
        {
            long[] p1 = Enumerable.Range(0, size).Select(i => (long)i).ToArray();
            _encoder.EncodeArray(p1);
            long[] r1 = _decoder.DecodeArray<long>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_decoder.Pos, _encoder.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Float(int size)
        {
            float[] p1 = Enumerable.Range(0, size).Select(i => (float)i).ToArray();
            _encoder.EncodeArray(p1);
            float[] r1 = _decoder.DecodeArray<float>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_decoder.Pos, _encoder.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Double(int size)
        {
            double[] p1 = Enumerable.Range(0, size).Select(i => (double)i).ToArray();
            _encoder.EncodeArray(p1);
            double[] r1 = _decoder.DecodeArray<double>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_decoder.Pos, _encoder.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_String(int size)
        {
            IEnumerable<string> p1 = Enumerable.Range(0, size).Select(i => $"string-{i}");
            _encoder.EncodeSequence(p1, (encoder, value) => encoder.EncodeString(value));
            IEnumerable<string> r1 = _decoder.DecodeSequence(1, decoder => decoder.DecodeString());

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_decoder.Pos, _encoder.Tail.Offset);
        }
    }
}
