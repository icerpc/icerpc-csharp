// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Encoding
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [TestFixture("1.1")]
    [TestFixture("2.0")]
    [Parallelizable(scope: ParallelScope.All)]
    public class BuiltInTypesTests
    {
        private readonly IceRpc.Encoding _encoding;
        private readonly BufferWriter _bufferWriter;
        private readonly IceEncoder _encoder;
        private readonly IceDecoder _decoder;

        public BuiltInTypesTests(string encoding)
        {
            _encoding = IceRpc.Encoding.FromString(encoding);
            var buffer = new byte[256];
            _bufferWriter = new BufferWriter(buffer);
            _encoder = Payload.CreateIceEncoder(_encoding, _bufferWriter);
            _decoder = new IceDecoder(buffer, _encoding);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Encoding_Bool(bool p1)
        {
            _encoder.EncodeBool(p1);
            bool r1 = _decoder.DecodeBool();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _bufferWriter.Tail.Buffer);
            Assert.AreEqual(sizeof(bool), _bufferWriter.Tail.Offset);
            Assert.AreEqual(sizeof(bool), _decoder.Pos);
        }

        [TestCase(byte.MinValue)]
        [TestCase(0)]
        [TestCase(byte.MaxValue)]
        public void Encoding_Byte(byte p1)
        {
            _encoder.EncodeByte(p1);
            byte r1 = _decoder.DecodeByte();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _bufferWriter.Tail.Buffer);
            Assert.AreEqual(sizeof(byte), _bufferWriter.Tail.Offset);
            Assert.AreEqual(sizeof(byte), _decoder.Pos);
        }

        [TestCase(short.MinValue)]
        [TestCase(0)]
        [TestCase(short.MaxValue)]
        public void Encoding_Short(short p1)
        {
            _encoder.EncodeShort(p1);
            short r1 = _decoder.DecodeShort();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _bufferWriter.Tail.Buffer);
            Assert.AreEqual(sizeof(short), _bufferWriter.Tail.Offset);
            Assert.AreEqual(sizeof(short), _decoder.Pos);
        }

        [TestCase(ushort.MinValue)]
        [TestCase(ushort.MaxValue)]
        public void Encoding_UShort(ushort p1)
        {
            _encoder.EncodeUShort(p1);
            ushort r1 = _decoder.DecodeUShort();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _bufferWriter.Tail.Buffer);
            Assert.AreEqual(sizeof(ushort), _bufferWriter.Tail.Offset);
            Assert.AreEqual(sizeof(ushort), _decoder.Pos);
        }

        [TestCase(int.MinValue)]
        [TestCase(0)]
        [TestCase(int.MaxValue)]
        public void Encoding_Int(int p1)
        {
            _encoder.EncodeInt(p1);
            int r1 = _decoder.DecodeInt();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _bufferWriter.Tail.Buffer);
            Assert.AreEqual(sizeof(int), _bufferWriter.Tail.Offset);
            Assert.AreEqual(sizeof(int), _decoder.Pos);
        }

        [TestCase(uint.MinValue)]
        [TestCase((uint)0)]
        [TestCase(uint.MaxValue)]
        public void Encoding_UInt(uint p1)
        {
            _encoder.EncodeUInt(p1);
            uint r1 = _decoder.DecodeUInt();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _bufferWriter.Tail.Buffer);
        }

        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        public void Encoding_Long(long p1)
        {
            _encoder.EncodeLong(p1);

            long r1 = _decoder.DecodeLong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _bufferWriter.Tail.Buffer);
            Assert.AreEqual(sizeof(long), _bufferWriter.Tail.Offset);
            Assert.AreEqual(sizeof(long), _decoder.Pos);
        }

        [TestCase(ulong.MinValue)]
        [TestCase(ulong.MinValue)]
        public void Encoding_ULong(ulong p1)
        {
            _encoder.EncodeULong(p1);
            ulong r1 = _decoder.DecodeULong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _bufferWriter.Tail.Buffer);
        }

        [TestCase(EncodingDefinitions.VarULongMinValue)]
        [TestCase(EncodingDefinitions.VarULongMinValue)]
        public void Encoding_VarULong(ulong p1)
        {
            _encoder.EncodeVarULong(p1);
            ulong r1 = _decoder.DecodeVarULong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _bufferWriter.Tail.Buffer);
        }

        [TestCase(EncodingDefinitions.VarLongMinValue)]
        [TestCase(EncodingDefinitions.VarLongMinValue)]
        public void Encoding_VarLong(long p1)
        {
            _encoder.EncodeVarLong(p1);
            long r1 = _decoder.DecodeVarLong();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _bufferWriter.Tail.Buffer);
        }

        [TestCase(float.MinValue)]
        [TestCase(0)]
        [TestCase(float.MaxValue)]
        public void Encoding_Float(float p1)
        {
            _encoder.EncodeFloat(p1);
            float r1 = _decoder.DecodeFloat();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _bufferWriter.Tail.Buffer);
            Assert.AreEqual(sizeof(float), _bufferWriter.Tail.Offset);
            Assert.AreEqual(sizeof(float), _decoder.Pos);
        }

        [TestCase(double.MinValue)]
        [TestCase(0)]
        [TestCase(double.MaxValue)]
        public void Encoding_Double(double p1)
        {
            _encoder.EncodeDouble(p1);

            double r1 = _decoder.DecodeDouble();

            Assert.AreEqual(p1, r1);
            Assert.AreEqual(0, _bufferWriter.Tail.Buffer);
            Assert.AreEqual(sizeof(double), _bufferWriter.Tail.Offset);
            Assert.AreEqual(sizeof(double), _decoder.Pos);
        }

        [TestCase("")]
        [TestCase("Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit.")]
        [TestCase("국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다")] // Korean
        [TestCase("旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ")] // Japanese
        public void Encoding_String(string p1)
        {
            _encoder.EncodeString(p1);

            string r1 = _decoder.DecodeString();

            Assert.AreEqual(p1, r1);
        }
    }
}
