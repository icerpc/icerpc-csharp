// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace IceRpc.Tests.Encoding
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [TestFixture((byte)1, (byte)1)]
    [TestFixture((byte)2, (byte)0)]
    [Parallelizable(scope: ParallelScope.All)]
    public class BuiltInTypesTests
    {
        private IceRpc.Encoding _encoding;
        private List<ArraySegment<byte>> _data;
        private OutputStream _ostr;
        private InputStream _istr;

        public BuiltInTypesTests(byte encodingMajor, byte encodingMinor)
        {
            _encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            _data = new List<ArraySegment<byte>>() { new byte[256] };
            _ostr = new OutputStream(_encoding, _data);
            _istr = new InputStream(_data[0], _encoding);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Encoding_Bool(bool p1)
        {
            _ostr.WriteBool(p1);
            bool r1 = _istr.ReadBool();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _ostr.Tail.Segment);
            Assert.AreEqual(sizeof(bool), _ostr.Tail.Offset);
            Assert.AreEqual(sizeof(bool), _istr.Pos);
        }

        [TestCase(byte.MinValue)]
        [TestCase(0)]
        [TestCase(byte.MaxValue)]
        public void Encoding_Byte(byte p1)
        {
            _ostr.WriteByte(p1);
            byte r1 = _istr.ReadByte();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _ostr.Tail.Segment);
            Assert.AreEqual(sizeof(byte), _ostr.Tail.Offset);
            Assert.AreEqual(sizeof(byte), _istr.Pos);
        }

        [TestCase(short.MinValue)]
        [TestCase(0)]
        [TestCase(short.MaxValue)]
        public void Encoding_Short(short p1)
        {
            _ostr.WriteShort(p1);
            short r1 = _istr.ReadShort();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _ostr.Tail.Segment);
            Assert.AreEqual(sizeof(short), _ostr.Tail.Offset);
            Assert.AreEqual(sizeof(short), _istr.Pos);
        }

        [TestCase(ushort.MinValue)]
        [TestCase(ushort.MaxValue)]
        public void Encoding_UShort(ushort p1)
        {
            _ostr.WriteUShort(p1);
            ushort r1 = _istr.ReadUShort();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _ostr.Tail.Segment);
            Assert.AreEqual(sizeof(ushort), _ostr.Tail.Offset);
            Assert.AreEqual(sizeof(ushort), _istr.Pos);
        }

        [TestCase(int.MinValue)]
        [TestCase(0)]
        [TestCase(int.MaxValue)]
        public void Encoding_Int(int p1)
        {
            _ostr.WriteInt(p1);
            int r1 = _istr.ReadInt();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _ostr.Tail.Segment);
            Assert.AreEqual(sizeof(int), _ostr.Tail.Offset);
            Assert.AreEqual(sizeof(int), _istr.Pos);
        }

        [TestCase(uint.MinValue)]
        [TestCase((uint)0)]
        [TestCase(uint.MaxValue)]
        public void Encoding_UInt(uint p1)
        {
            _ostr.WriteUInt(p1);
            uint r1 = _istr.ReadUInt();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _ostr.Tail.Segment);
        }

        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        public void Encoding_Long(long p1)
        {
            _ostr.WriteLong(p1);

            long r1 = _istr.ReadLong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _ostr.Tail.Segment);
            Assert.AreEqual(sizeof(long), _ostr.Tail.Offset);
            Assert.AreEqual(sizeof(long), _istr.Pos);
        }

        [TestCase(ulong.MinValue)]
        [TestCase(ulong.MinValue)]
        public void Encoding_ULong(ulong p1)
        {
            _ostr.WriteULong(p1);
            ulong r1 = _istr.ReadULong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _ostr.Tail.Segment);
        }

        [TestCase(EncodingDefinitions.VarULongMinValue)]
        [TestCase(EncodingDefinitions.VarULongMinValue)]
        public void Encoding_VarULong(ulong p1)
        {
            _ostr.WriteVarULong(p1);
            ulong r1 = _istr.ReadVarULong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _ostr.Tail.Segment);
        }

        [TestCase(EncodingDefinitions.VarLongMinValue)]
        [TestCase(EncodingDefinitions.VarLongMinValue)]
        public void Encoding_VarLong(long p1)
        {
            _ostr.WriteVarLong(p1);
            long r1 = _istr.ReadVarLong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _ostr.Tail.Segment);
        }

        [TestCase(float.MinValue)]
        [TestCase(0)]
        [TestCase(float.MaxValue)]
        public void Encoding_Float(float p1)
        {
            _ostr.WriteFloat(p1);
            float r1 = _istr.ReadFloat();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _ostr.Tail.Segment);
            Assert.AreEqual(sizeof(float), _ostr.Tail.Offset);
            Assert.AreEqual(sizeof(float), _istr.Pos);
        }

        [TestCase(double.MinValue)]
        [TestCase(0)]
        [TestCase(double.MaxValue)]
        public void Encoding_Double(double p1)
        {
            _ostr.WriteDouble(p1);

            double r1 = _istr.ReadDouble();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _ostr.Tail.Segment);
            Assert.AreEqual(sizeof(double), _ostr.Tail.Offset);
            Assert.AreEqual(sizeof(double), _istr.Pos);
        }

        [TestCase("")]
        [TestCase("Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit.")]
        [TestCase("국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다")] // Korean
        [TestCase("旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ")] // Japanese
        public void Encoding_String(string p1)
        {
            _ostr.WriteString(p1);

            string r1 = _istr.ReadString();

            Assert.AreEqual(p1, r1);
        }
    }
}
