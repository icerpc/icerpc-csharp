// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;
using System;

namespace IceRpc.Tests.Encoding
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [TestFixture((byte)1, (byte)1)]
    [TestFixture((byte)2, (byte)0)]
    [Parallelizable(scope: ParallelScope.All)]
    public class BuiltInTypesTests
    {
        private readonly IceRpc.Encoding _encoding;
        private readonly Memory<byte> _buffer;
        private readonly IceEncoder _iceEncoder;
        private readonly IceDecoder _iceDecoder;

        public BuiltInTypesTests(byte encodingMajor, byte encodingMinor)
        {
            _encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            _buffer = new byte[256];
            _iceEncoder = new IceEncoder(_encoding, _buffer);
            _iceDecoder = new IceDecoder(_buffer, _encoding);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Encoding_Bool(bool p1)
        {
            _iceEncoder.WriteBool(p1);
            bool r1 = _iceDecoder.DecodeBool();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _iceEncoder.Tail.Buffer);
            Assert.AreEqual(sizeof(bool), _iceEncoder.Tail.Offset);
            Assert.AreEqual(sizeof(bool), _iceDecoder.Pos);
        }

        [TestCase(byte.MinValue)]
        [TestCase(0)]
        [TestCase(byte.MaxValue)]
        public void Encoding_Byte(byte p1)
        {
            _iceEncoder.WriteByte(p1);
            byte r1 = _iceDecoder.DecodeByte();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _iceEncoder.Tail.Buffer);
            Assert.AreEqual(sizeof(byte), _iceEncoder.Tail.Offset);
            Assert.AreEqual(sizeof(byte), _iceDecoder.Pos);
        }

        [TestCase(short.MinValue)]
        [TestCase(0)]
        [TestCase(short.MaxValue)]
        public void Encoding_Short(short p1)
        {
            _iceEncoder.WriteShort(p1);
            short r1 = _iceDecoder.DecodeShort();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _iceEncoder.Tail.Buffer);
            Assert.AreEqual(sizeof(short), _iceEncoder.Tail.Offset);
            Assert.AreEqual(sizeof(short), _iceDecoder.Pos);
        }

        [TestCase(ushort.MinValue)]
        [TestCase(ushort.MaxValue)]
        public void Encoding_UShort(ushort p1)
        {
            _iceEncoder.WriteUShort(p1);
            ushort r1 = _iceDecoder.DecodeUShort();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _iceEncoder.Tail.Buffer);
            Assert.AreEqual(sizeof(ushort), _iceEncoder.Tail.Offset);
            Assert.AreEqual(sizeof(ushort), _iceDecoder.Pos);
        }

        [TestCase(int.MinValue)]
        [TestCase(0)]
        [TestCase(int.MaxValue)]
        public void Encoding_Int(int p1)
        {
            _iceEncoder.WriteInt(p1);
            int r1 = _iceDecoder.DecodeInt();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _iceEncoder.Tail.Buffer);
            Assert.AreEqual(sizeof(int), _iceEncoder.Tail.Offset);
            Assert.AreEqual(sizeof(int), _iceDecoder.Pos);
        }

        [TestCase(uint.MinValue)]
        [TestCase((uint)0)]
        [TestCase(uint.MaxValue)]
        public void Encoding_UInt(uint p1)
        {
            _iceEncoder.WriteUInt(p1);
            uint r1 = _iceDecoder.DecodeUInt();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _iceEncoder.Tail.Buffer);
        }

        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        public void Encoding_Long(long p1)
        {
            _iceEncoder.WriteLong(p1);

            long r1 = _iceDecoder.DecodeLong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _iceEncoder.Tail.Buffer);
            Assert.AreEqual(sizeof(long), _iceEncoder.Tail.Offset);
            Assert.AreEqual(sizeof(long), _iceDecoder.Pos);
        }

        [TestCase(ulong.MinValue)]
        [TestCase(ulong.MinValue)]
        public void Encoding_ULong(ulong p1)
        {
            _iceEncoder.WriteULong(p1);
            ulong r1 = _iceDecoder.DecodeULong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _iceEncoder.Tail.Buffer);
        }

        [TestCase(EncodingDefinitions.VarULongMinValue)]
        [TestCase(EncodingDefinitions.VarULongMinValue)]
        public void Encoding_VarULong(ulong p1)
        {
            _iceEncoder.WriteVarULong(p1);
            ulong r1 = _iceDecoder.DecodeVarULong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _iceEncoder.Tail.Buffer);
        }

        [TestCase(EncodingDefinitions.VarLongMinValue)]
        [TestCase(EncodingDefinitions.VarLongMinValue)]
        public void Encoding_VarLong(long p1)
        {
            _iceEncoder.WriteVarLong(p1);
            long r1 = _iceDecoder.DecodeVarLong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _iceEncoder.Tail.Buffer);
        }

        [TestCase(float.MinValue)]
        [TestCase(0)]
        [TestCase(float.MaxValue)]
        public void Encoding_Float(float p1)
        {
            _iceEncoder.WriteFloat(p1);
            float r1 = _iceDecoder.DecodeFloat();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _iceEncoder.Tail.Buffer);
            Assert.AreEqual(sizeof(float), _iceEncoder.Tail.Offset);
            Assert.AreEqual(sizeof(float), _iceDecoder.Pos);
        }

        [TestCase(double.MinValue)]
        [TestCase(0)]
        [TestCase(double.MaxValue)]
        public void Encoding_Double(double p1)
        {
            _iceEncoder.WriteDouble(p1);

            double r1 = _iceDecoder.DecodeDouble();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _iceEncoder.Tail.Buffer);
            Assert.AreEqual(sizeof(double), _iceEncoder.Tail.Offset);
            Assert.AreEqual(sizeof(double), _iceDecoder.Pos);
        }

        [TestCase("")]
        [TestCase("Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit.")]
        [TestCase("국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다")] // Korean
        [TestCase("旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ")] // Japanese
        public void Encoding_String(string p1)
        {
            _iceEncoder.WriteString(p1);

            string r1 = _iceDecoder.DecodeString();

            Assert.AreEqual(p1, r1);
        }
    }
}
