// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Tests;
using NUnit.Framework;
using System.IO.Pipelines;
using System.Buffers;
using IceRpc.Internal;

namespace IceRpc.Slice.Tests;

/// <summary>Test decoding strings with the supported Slice encodings.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class DecodeStringTests
{
    /// <summary>Tests the decoding of a string. The only difference between decoding strings with the 1.1 encoding and
    /// the 2.0 encoding is how the size gets encoded. Since <see cref="Decoding_size(string, byte[], byte[])"/>
    /// tests the size encoding, this test only needs to verify how strings are decoded with 2.0. </summary>
    /// <param name="testString">The string to be decoded.</param>
    [TestCase("")]
    [TestCase("Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit.")]
    [TestCase("국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다")] // Korean
    [TestCase("旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ")]  // Japanese
    [TestCase("😁😂😃😄😅😆😉😊😋😌😍😏😒😓😔😖")]
    public void Decode_string(string testString)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice20);
        encoder.EncodeString(testString);
        byte[] encodedString = buffer[0..bufferWriter.WrittenMemory.Length];
        var sut = new SliceDecoder(encodedString, SliceEncoding.Slice20);

        var r1 = sut.DecodeString();

        Assert.That(r1, Is.EqualTo(testString));
        Assert.That(sut.Consumed, Is.EqualTo(encodedString.Length));
    }

    [Test]
    public void Decode_single_segment_non_utf8_string_fails()
    {
        Assert.That(() =>
        {
            var encodedString = new byte[] { 0x08, 0xFD, 0xFF }; // Byte array for unicode char \uD800
            var sut = new SliceDecoder(encodedString, SliceEncoding.Slice20);

            sut.DecodeString();
        }, Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void Decode_multi_segment_non_utf8_string_fails()
    {
        Assert.That(() =>
        {
            // Arrange
            // A custom memory pool with a tiny max buffer size
            using var customPool = new TestMemoryPool(7);

            // minimumSegmentSize is not the same as the sizeHint given to GetMemory/GetSpan; it refers to the
            // minBufferSize given to Rent
            var pipe = new Pipe(new PipeOptions(pool: customPool, minimumSegmentSize: 5));
            var p1 = System.Text.Encoding.UTF8.GetBytes("This is a bad string with unicode characters");
            var badBytes = new byte[] { 0xFE, 0xFF };
            var size = p1.Length + badBytes.Length;

            pipe.Writer.Write(new byte[] { (byte)(size << 2) });
            pipe.Writer.Write(p1);
            pipe.Writer.Write(badBytes);
            pipe.Writer.Complete();
            pipe.Reader.TryRead(out ReadResult readResult);

            var sut = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice20);

            // Act
            var result = sut.DecodeString();

        }, Throws.InstanceOf<InvalidDataException>());
    }
}
