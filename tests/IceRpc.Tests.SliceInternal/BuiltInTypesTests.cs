// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Buffers;
using System.Diagnostics;
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
        private readonly IceEncoding _encoding;
        private readonly SingleBufferWriter _bufferWriter;
        private readonly IceEncoder _encoder;

        public BuiltInTypesTests(string encoding)
        {
            _encoding = IceEncoding.FromString(encoding);
            _buffer = new byte[256];
            _bufferWriter = new SingleBufferWriter(_buffer);
            _encoder = _encoding.CreateIceEncoder(_bufferWriter);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Encoding_Bool(bool p1)
        {
            var decoder = new IceDecoder(_buffer, _encoding);

            _encoder.EncodeBool(p1);
            bool r1 = decoder.DecodeBool();

            Assert.AreEqual(p1, r1);
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(bool)));
            Assert.AreEqual(sizeof(bool), decoder.Consumed);
        }

        [TestCase(byte.MinValue)]
        [TestCase(0)]
        [TestCase(byte.MaxValue)]
        public void Encoding_Byte(byte p1)
        {
            var decoder = new IceDecoder(_buffer, _encoding);
            _encoder.EncodeByte(p1);
            byte r1 = decoder.DecodeByte();

            Assert.AreEqual(p1, r1);
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(byte)));
            Assert.AreEqual(sizeof(byte), decoder.Consumed);
        }

        [TestCase(short.MinValue)]
        [TestCase(0)]
        [TestCase(short.MaxValue)]
        public void Encoding_Short(short p1)
        {
            var decoder = new IceDecoder(_buffer, _encoding);
            _encoder.EncodeShort(p1);
            short r1 = decoder.DecodeShort();

            Assert.AreEqual(p1, r1);
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(short)));
            Assert.AreEqual(sizeof(short), decoder.Consumed);
        }

        [TestCase(ushort.MinValue)]
        [TestCase(ushort.MaxValue)]
        public void Encoding_UShort(ushort p1)
        {
            var decoder = new IceDecoder(_buffer, _encoding);
            _encoder.EncodeUShort(p1);
            ushort r1 = decoder.DecodeUShort();

            Assert.AreEqual(p1, r1);
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(ushort)));
            Assert.AreEqual(sizeof(ushort), decoder.Consumed);
        }

        [TestCase(int.MinValue)]
        [TestCase(0)]
        [TestCase(int.MaxValue)]
        public void Encoding_Int(int p1)
        {
            var decoder = new IceDecoder(_buffer, _encoding);
            _encoder.EncodeInt(p1);
            int r1 = decoder.DecodeInt();

            Assert.AreEqual(p1, r1);
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(int)));
            Assert.AreEqual(sizeof(int), decoder.Consumed);
        }

        [TestCase(uint.MinValue)]
        [TestCase((uint)0)]
        [TestCase(uint.MaxValue)]
        public void Encoding_UInt(uint p1)
        {
            var decoder = new IceDecoder(_buffer, _encoding);
            _encoder.EncodeUInt(p1);
            uint r1 = decoder.DecodeUInt();

            Assert.AreEqual(p1, r1);
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(uint)));
        }

        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        public void Encoding_Long(long p1)
        {
            var decoder = new IceDecoder(_buffer, _encoding);
            _encoder.EncodeLong(p1);

            long r1 = decoder.DecodeLong();

            Assert.AreEqual(p1, r1);
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(long)));
            Assert.AreEqual(sizeof(long), decoder.Consumed);
        }

        [TestCase(ulong.MinValue)]
        [TestCase(ulong.MinValue)]
        public void Encoding_ULong(ulong p1)
        {
            var decoder = new IceDecoder(_buffer, _encoding);
            _encoder.EncodeULong(p1);
            ulong r1 = decoder.DecodeULong();

            Assert.AreEqual(p1, r1);
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(ulong)));
        }

        [TestCase(IceEncoder.VarULongMinValue)]
        [TestCase(IceEncoder.VarULongMinValue)]
        public void Encoding_VarULong(ulong p1)
        {
            var decoder = new IceDecoder(_buffer, _encoding);
            _encoder.EncodeVarULong(p1);
            ulong r1 = decoder.DecodeVarULong();

            Assert.AreEqual(p1, r1);
        }

        [TestCase(IceEncoder.VarLongMinValue)]
        [TestCase(IceEncoder.VarLongMinValue)]
        public void Encoding_VarLong(long p1)
        {
            var decoder = new IceDecoder(_buffer, _encoding);
            _encoder.EncodeVarLong(p1);
            long r1 = decoder.DecodeVarLong();

            Assert.AreEqual(p1, r1);
        }

        [TestCase(float.MinValue)]
        [TestCase(0)]
        [TestCase(float.MaxValue)]
        public void Encoding_Float(float p1)
        {
            var decoder = new IceDecoder(_buffer, _encoding);
            _encoder.EncodeFloat(p1);
            float r1 = decoder.DecodeFloat();

            Assert.AreEqual(p1, r1);
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(float)));
            Assert.AreEqual(sizeof(float), decoder.Consumed);
        }

        [TestCase(double.MinValue)]
        [TestCase(0)]
        [TestCase(double.MaxValue)]
        public void Encoding_Double(double p1)
        {
            var decoder = new IceDecoder(_buffer, _encoding);
            _encoder.EncodeDouble(p1);

            double r1 = decoder.DecodeDouble();

            Assert.AreEqual(p1, r1);
            Assert.That(_bufferWriter.WrittenBuffer.Length, Is.EqualTo(sizeof(double)));
            Assert.AreEqual(sizeof(double), decoder.Consumed);
        }

        [TestCase("")]
        [TestCase("Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit.")]
        [TestCase("국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다")] // Korean
        [TestCase("旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ")] // Japanese
        [TestCase("😁😂😃😄😅😆😉😊😋😌😍😏😒😓😔😖")] // each character encoded with surrogates
        public void Encoding_String(string p1)
        {
            var decoder = new IceDecoder(_buffer, _encoding);

            // simple test
            _encoder.EncodeString(p1);
            string r1 = decoder.DecodeString();
            Assert.AreEqual(p1, r1);

            if (p1.Length > 0)
            {
                // now with a custom memory pool with a tiny max buffer size
                using var customPool = new TestMemoryPool(7);

                // minimumSegmentSize is not the same as the sizeHint given to GetMemory/GetSpan; it refers to the
                // minBufferSize given to Rent
                var pipe = new Pipe(new PipeOptions(pool: customPool, minimumSegmentSize: 5));

                IceEncoder encoder = _encoding.CreateIceEncoder(pipe.Writer);
                for (int i = 0; i < 20; ++i)
                {
                    encoder.EncodeString(p1);
                }
                pipe.Writer.Complete();

                pipe.Reader.TryRead(out ReadResult readResult);
                ReadOnlyMemory<byte> buffer = readResult.Buffer.ToSingleBuffer();
                decoder = new IceDecoder(buffer, _encoding);

                for (int i = 0; i < 20; ++i)
                {
                    r1 = decoder.DecodeString();
                    Assert.AreEqual(p1, r1);
                }
            }
        }

        private class TestMemoryPool : MemoryPool<byte>
        {
            public override int MaxBufferSize { get; }

            public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
            {
                Debug.Assert(minBufferSize < MaxBufferSize);
                return new MemoryOwnerDecorator(Shared.Rent(minBufferSize), MaxBufferSize);
            }

            protected override void Dispose(bool disposing)
            {
                // no-op
            }

            internal TestMemoryPool(int maxBufferSize) => MaxBufferSize = maxBufferSize;
        }

        private sealed class MemoryOwnerDecorator : IMemoryOwner<byte>
        {
            public Memory<byte> Memory { get; }

            public void Dispose() => _decoratee.Dispose();

            private readonly IMemoryOwner<byte> _decoratee;

            internal MemoryOwnerDecorator(IMemoryOwner<byte> decoratee, int maxBufferSize)
            {
                _decoratee = decoratee;
                Memory = decoratee.Memory.Length > maxBufferSize ?
                    decoratee.Memory[0..maxBufferSize] : decoratee.Memory;
            }
        }
    }
}
