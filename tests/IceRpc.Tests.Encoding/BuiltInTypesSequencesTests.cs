﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
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
        private IceRpc.Encoding _encoding;
        private byte[] _buffer;
        private OutputStream _ostr;
        private InputStream _istr;

        public BuiltInTypesSequencesTests(byte encodingMajor, byte encodingMinor)
        {
            _encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            _buffer = new byte[1024 * 1024];
            _ostr = new OutputStream(_encoding, _buffer);
            _istr = new InputStream(_buffer, _encoding);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Bool(int size)
        {
            bool[] p1 = Enumerable.Range(0, size).Select(i => i % 2 == 0).ToArray();
            _ostr.WriteArray(p1);
            bool[] r1 = _istr.ReadArray<bool>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_istr.Pos, _ostr.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Byte(int size)
        {
            byte[] p1 = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
            _ostr.WriteArray(p1);
            byte[] r1 = _istr.ReadArray<byte>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_istr.Pos, _ostr.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Short(int size)
        {
            short[] p1 = Enumerable.Range(0, size).Select(i => (short)i).ToArray();
            _ostr.WriteArray(p1);
            short[] r1 = _istr.ReadArray<short>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_istr.Pos, _ostr.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Int(int size)
        {
            int[] p1 = Enumerable.Range(0, size).ToArray();
            _ostr.WriteArray(p1);
            int[] r1 = _istr.ReadArray<int>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_istr.Pos, _ostr.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Long(int size)
        {
            long[] p1 = Enumerable.Range(0, size).Select(i => (long)i).ToArray();
            _ostr.WriteArray(p1);
            long[] r1 = _istr.ReadArray<long>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_istr.Pos, _ostr.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Float(int size)
        {
            float[] p1 = Enumerable.Range(0, size).Select(i => (float)i).ToArray();
            _ostr.WriteArray(p1);
            float[] r1 = _istr.ReadArray<float>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_istr.Pos, _ostr.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_Double(int size)
        {
            double[] p1 = Enumerable.Range(0, size).Select(i => (double)i).ToArray();
            _ostr.WriteArray(p1);
            double[] r1 = _istr.ReadArray<double>();

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_istr.Pos, _ostr.Tail.Offset);
        }

        [TestCase(0)]
        [TestCase(256)]
        public void BuiltInTypesSequences_String(int size)
        {
            IEnumerable<string> p1 = Enumerable.Range(0, size).Select(i => $"string-{i}");
            _ostr.WriteSequence(p1, OutputStream.IceWriterFromString);
            IEnumerable<string> r1 = _istr.ReadSequence(1, InputStream.IceReaderIntoString);

            CollectionAssert.AreEqual(p1, r1);
            Assert.AreEqual(_istr.Pos, _ostr.Tail.Offset);
        }
    }
}
