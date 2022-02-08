// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Tests.SliceInternal
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [TestFixture("1.1")]
    [TestFixture("2.0")]
    [Parallelizable(scope: ParallelScope.All)]
    public class BuiltInTypesTests
    {
        private readonly Memory<byte> _buffer;
        private readonly SliceEncoding _encoding;
        private readonly SingleBufferWriter _bufferWriter;

        public BuiltInTypesTests(string encoding)
        {
            _encoding = SliceEncoding.FromString(encoding);
            _buffer = new byte[256];
            _bufferWriter = new SingleBufferWriter(_buffer);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Encoding_Bool(bool p1)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);

            encoder.EncodeBool(p1);
            bool r1 = decoder.DecodeBool();

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(bool)));
            Assert.That(decoder.Consumed, Is.EqualTo(sizeof(bool)));

        }

        [TestCase(byte.MinValue)]
        [TestCase(0)]
        [TestCase(byte.MaxValue)]
        public void Encoding_Byte(byte p1)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);
            encoder.EncodeByte(p1);
            byte r1 = decoder.DecodeByte();

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(byte)));
            Assert.That(decoder.Consumed, Is.EqualTo(sizeof(byte)));
        }

        [TestCase(short.MinValue)]
        [TestCase(0)]
        [TestCase(short.MaxValue)]
        public void Encoding_Short(short p1)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);
            encoder.EncodeShort(p1);
            short r1 = decoder.DecodeShort();

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(short)));
            Assert.That(decoder.Consumed, Is.EqualTo(sizeof(short)));
        }

        [TestCase(ushort.MinValue)]
        [TestCase(ushort.MaxValue)]
        public void Encoding_UShort(ushort p1)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);
            encoder.EncodeUShort(p1);
            ushort r1 = decoder.DecodeUShort();

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(ushort)));
            Assert.That(decoder.Consumed, Is.EqualTo(sizeof(ushort)));
        }

        [TestCase(int.MinValue)]
        [TestCase(0)]
        [TestCase(int.MaxValue)]
        public void Encoding_Int(int p1)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);
            encoder.EncodeInt(p1);
            int r1 = decoder.DecodeInt();

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(int)));
            Assert.That(decoder.Consumed, Is.EqualTo(sizeof(int)));
        }

        [TestCase(uint.MinValue)]
        [TestCase((uint)0)]
        [TestCase(uint.MaxValue)]
        public void Encoding_UInt(uint p1)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);
            encoder.EncodeUInt(p1);
            uint r1 = decoder.DecodeUInt();

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(uint)));
        }

        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        public void Encoding_Long(long p1)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);
            encoder.EncodeLong(p1);

            long r1 = decoder.DecodeLong();

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(long)));
            Assert.That(decoder.Consumed, Is.EqualTo(sizeof(long)));
        }

        [TestCase(ulong.MinValue)]
        [TestCase(ulong.MaxValue)]
        public void Encoding_ULong(ulong p1)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);
            encoder.EncodeULong(p1);
            ulong r1 = decoder.DecodeULong();

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(ulong)));
        }

        [TestCase(SliceEncoder.VarULongMinValue)]
        [TestCase(SliceEncoder.VarULongMaxValue)]
        public void Encoding_VarULong(ulong p1)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);
            encoder.EncodeVarULong(p1);
            ulong r1 = decoder.DecodeVarULong();

            Assert.That(p1, Is.EqualTo(r1));
        }

        [TestCase(SliceEncoder.VarLongMinValue)]
        [TestCase(SliceEncoder.VarLongMaxValue)]
        public void Encoding_VarLong(long p1)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);
            encoder.EncodeVarLong(p1);
            long r1 = decoder.DecodeVarLong();

            Assert.That(p1, Is.EqualTo(r1));
        }

        [TestCase(float.MinValue)]
        [TestCase(0)]
        [TestCase(float.MaxValue)]
        public void Encoding_Float(float p1)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);
            encoder.EncodeFloat(p1);
            float r1 = decoder.DecodeFloat();

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(float)));
            Assert.That(decoder.Consumed, Is.EqualTo(sizeof(float)));
        }

        [TestCase(double.MinValue)]
        [TestCase(0)]
        [TestCase(double.MaxValue)]
        public void Encoding_Double(double p1)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);
            encoder.EncodeDouble(p1);

            double r1 = decoder.DecodeDouble();

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(double)));
            Assert.That(decoder.Consumed, Is.EqualTo(sizeof(double)));
        }

        [TestCase("")]
        [TestCase("Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit.")]
        [TestCase("국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다")] // Korean
        [TestCase("旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ")] // Japanese
        [TestCase("😁😂😃😄😅😆😉😊😋😌😍😏😒😓😔😖")] // each character encoded with surrogates
        public void Encoding_String(string p1)
        {
            var encoder = new SliceEncoder(_bufferWriter, _encoding);
            var decoder = new SliceDecoder(_buffer, _encoding);

            // simple test
            encoder.EncodeString(p1);
            string r1 = decoder.DecodeString();
            Assert.That(p1, Is.EqualTo(r1));

            if (p1.Length > 0)
            {
                // now with a custom memory pool with a tiny max buffer size
                using var customPool = new TestMemoryPool(7);

                // minimumSegmentSize is not the same as the sizeHint given to GetMemory/GetSpan; it refers to the
                // minBufferSize given to Rent
                var pipe = new Pipe(new PipeOptions(pool: customPool, minimumSegmentSize: 5));

                encoder = new SliceEncoder(pipe.Writer, _encoding);
                for (int i = 0; i < 20; ++i)
                {
                    encoder.EncodeString(p1);
                }
                pipe.Writer.Complete();

                pipe.Reader.TryRead(out ReadResult readResult);
                decoder = new SliceDecoder(readResult.Buffer, _encoding);

                for (int i = 0; i < 20; ++i)
                {
                    r1 = decoder.DecodeString();
                    Assert.That(p1, Is.EqualTo(r1));
                }
                pipe.Reader.AdvanceTo(readResult.Buffer.End);
            }
        }
    }
}
