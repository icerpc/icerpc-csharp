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
        private readonly IceEncoder _writer;
        private readonly IceDecoder _reader;

        public BuiltInTypesTests(byte encodingMajor, byte encodingMinor)
        {
            _encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            _buffer = new byte[256];
            _writer = new IceEncoder(_encoding, _buffer);
            _reader = new IceDecoder(_buffer, _encoding);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Encoding_Bool(bool p1)
        {
            _writer.WriteBool(p1);
            bool r1 = _reader.ReadBool();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _writer.Tail.Buffer);
            Assert.AreEqual(sizeof(bool), _writer.Tail.Offset);
            Assert.AreEqual(sizeof(bool), _reader.Pos);
        }

        [TestCase(byte.MinValue)]
        [TestCase(0)]
        [TestCase(byte.MaxValue)]
        public void Encoding_Byte(byte p1)
        {
            _writer.WriteByte(p1);
            byte r1 = _reader.ReadByte();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _writer.Tail.Buffer);
            Assert.AreEqual(sizeof(byte), _writer.Tail.Offset);
            Assert.AreEqual(sizeof(byte), _reader.Pos);
        }

        [TestCase(short.MinValue)]
        [TestCase(0)]
        [TestCase(short.MaxValue)]
        public void Encoding_Short(short p1)
        {
            _writer.WriteShort(p1);
            short r1 = _reader.ReadShort();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _writer.Tail.Buffer);
            Assert.AreEqual(sizeof(short), _writer.Tail.Offset);
            Assert.AreEqual(sizeof(short), _reader.Pos);
        }

        [TestCase(ushort.MinValue)]
        [TestCase(ushort.MaxValue)]
        public void Encoding_UShort(ushort p1)
        {
            _writer.WriteUShort(p1);
            ushort r1 = _reader.ReadUShort();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _writer.Tail.Buffer);
            Assert.AreEqual(sizeof(ushort), _writer.Tail.Offset);
            Assert.AreEqual(sizeof(ushort), _reader.Pos);
        }

        [TestCase(int.MinValue)]
        [TestCase(0)]
        [TestCase(int.MaxValue)]
        public void Encoding_Int(int p1)
        {
            _writer.WriteInt(p1);
            int r1 = _reader.ReadInt();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _writer.Tail.Buffer);
            Assert.AreEqual(sizeof(int), _writer.Tail.Offset);
            Assert.AreEqual(sizeof(int), _reader.Pos);
        }

        [TestCase(uint.MinValue)]
        [TestCase((uint)0)]
        [TestCase(uint.MaxValue)]
        public void Encoding_UInt(uint p1)
        {
            _writer.WriteUInt(p1);
            uint r1 = _reader.ReadUInt();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _writer.Tail.Buffer);
        }

        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        public void Encoding_Long(long p1)
        {
            _writer.WriteLong(p1);

            long r1 = _reader.ReadLong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _writer.Tail.Buffer);
            Assert.AreEqual(sizeof(long), _writer.Tail.Offset);
            Assert.AreEqual(sizeof(long), _reader.Pos);
        }

        [TestCase(ulong.MinValue)]
        [TestCase(ulong.MinValue)]
        public void Encoding_ULong(ulong p1)
        {
            _writer.WriteULong(p1);
            ulong r1 = _reader.ReadULong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _writer.Tail.Buffer);
        }

        [TestCase(EncodingDefinitions.VarULongMinValue)]
        [TestCase(EncodingDefinitions.VarULongMinValue)]
        public void Encoding_VarULong(ulong p1)
        {
            _writer.WriteVarULong(p1);
            ulong r1 = _reader.ReadVarULong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _writer.Tail.Buffer);
        }

        [TestCase(EncodingDefinitions.VarLongMinValue)]
        [TestCase(EncodingDefinitions.VarLongMinValue)]
        public void Encoding_VarLong(long p1)
        {
            _writer.WriteVarLong(p1);
            long r1 = _reader.ReadVarLong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _writer.Tail.Buffer);
        }

        [TestCase(float.MinValue)]
        [TestCase(0)]
        [TestCase(float.MaxValue)]
        public void Encoding_Float(float p1)
        {
            _writer.WriteFloat(p1);
            float r1 = _reader.ReadFloat();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _writer.Tail.Buffer);
            Assert.AreEqual(sizeof(float), _writer.Tail.Offset);
            Assert.AreEqual(sizeof(float), _reader.Pos);
        }

        [TestCase(double.MinValue)]
        [TestCase(0)]
        [TestCase(double.MaxValue)]
        public void Encoding_Double(double p1)
        {
            _writer.WriteDouble(p1);

            double r1 = _reader.ReadDouble();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _writer.Tail.Buffer);
            Assert.AreEqual(sizeof(double), _writer.Tail.Offset);
            Assert.AreEqual(sizeof(double), _reader.Pos);
        }

        [TestCase("")]
        [TestCase("Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit.")]
        [TestCase("국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다")] // Korean
        [TestCase("旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ")] // Japanese
        public void Encoding_String(string p1)
        {
            _writer.WriteString(p1);

            string r1 = _reader.ReadString();

            Assert.AreEqual(p1, r1);
        }
    }
}
