// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Tests;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

/// <summary>Test encoding of built-in types with the supported Slice encodings.</summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[TestFixture("1.1")]
[TestFixture("2.0")]
[Parallelizable(scope: ParallelScope.All)]
public class EncodingBuiltInTypesTests
{
    private readonly Memory<byte> _buffer;
    private readonly SliceEncoding _encoding;
    private readonly MemoryBufferWriter _bufferWriter;

    public EncodingBuiltInTypesTests(string encoding)
    {
        _encoding = SliceEncoding.FromString(encoding);
        _buffer = new byte[256];
        _bufferWriter = new MemoryBufferWriter(_buffer);
    }

    /// <summary>Test the encoding of a fixed size numeric type.</summary>
    /// <param name="p1">The encoded value.</param>
    [TestCase(long.MinValue)]
    [TestCase(long.MaxValue)]
    public void Encoding_Long(long p1)
    {
        var encoder = new SliceEncoder(_bufferWriter, _encoding);
        var decoder = new SliceDecoder(_buffer, _encoding);
        encoder.EncodeLong(p1);

        long r1 = decoder.DecodeLong();

        Assert.That(p1, Is.EqualTo(r1));
        Assert.That(_bufferWriter.WrittenMemory.Length, Is.EqualTo(sizeof(long)));
        Assert.That(decoder.Consumed, Is.EqualTo(sizeof(long)));
    }

    /// <summary>Test the encoding of a variable size numeric type.</summary>
    /// <param name="p1">The encoded value.</param>
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
