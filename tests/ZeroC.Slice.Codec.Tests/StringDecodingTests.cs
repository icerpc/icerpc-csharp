// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using ZeroC.Tests.Common;

namespace ZeroC.Slice.Codec.Tests;

/// <summary>Test decoding strings.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class DecodeStringTests
{
    /// <summary>Tests the decoding of a string.</summary>
    /// <param name="testString">The string to be decoded.</param>
    [TestCase("")]
    [TestCase("Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit.")] // cspell:disable-line
    [TestCase("국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다")] // Korean
    [TestCase("旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ")]  // Japanese
    [TestCase("😁😂😃😄😅😆😉😊😋😌😍😏😒😓😔😖")]
    public void Decode_string(string testString)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter);
        encoder.EncodeString(testString);
        byte[] encodedString = buffer[0..bufferWriter.WrittenMemory.Length];
        var sut = new SliceDecoder(encodedString);

        var r1 = sut.DecodeString();

        Assert.That(r1, Is.EqualTo(testString));
        Assert.That(sut.Consumed, Is.EqualTo(encodedString.Length));
    }

    [Test]
    public void Decode_non_utf8_string_fails()
    {
        Assert.That(() =>
        {
            var encodedString = new byte[] { 0x08, 0xFD, 0xFF }; // Byte array for unicode char \uD800
            var sut = new SliceDecoder(encodedString);

            _ = sut.DecodeString();
        }, Throws.InstanceOf<InvalidDataException>());
    }
}
