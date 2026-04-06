// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.Runtime.CompilerServices;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Codec.Tests;

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
        var encoder = new IceEncoder(bufferWriter);
        encoder.EncodeString(testString);
        byte[] encodedString = buffer[0..bufferWriter.WrittenMemory.Length];
        var sut = new IceDecoder(encodedString);

        var r1 = sut.DecodeString();

        Assert.That(r1, Is.EqualTo(testString));
        Assert.That(sut.Consumed, Is.EqualTo(encodedString.Length));
    }

    [Test]
    public void Decode_non_utf8_string_fails()
    {
        Assert.That(() =>
        {
            var encodedString = new byte[] { 0x02, 0xFD, 0xFF }; // Ice size 2 + invalid UTF-8 bytes
            var sut = new IceDecoder(encodedString);

            _ = sut.DecodeString();
        }, Throws.InstanceOf<InvalidDataException>());
    }

    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public void Decode_string_exceeds_max_collection_allocation(int length)
    {
        // Arrange
        string testString = new('a', length);
        var buffer = new MemoryBufferWriter(new byte[length + 256]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeString(testString);

        int allocationLimit = (length - 1) * Unsafe.SizeOf<char>();

        // Act/Assert
        Assert.That(
            () =>
            {
                var sut = new IceDecoder(buffer.WrittenMemory, maxCollectionAllocation: allocationLimit);
                _ = sut.DecodeString();
            },
            Throws.InstanceOf<InvalidDataException>());
    }

    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public void Decode_string_within_max_collection_allocation(int length)
    {
        // Arrange
        string testString = new('a', length);
        var buffer = new MemoryBufferWriter(new byte[length + 256]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeString(testString);

        int allocationLimit = length * Unsafe.SizeOf<char>();

        // Act/Assert
        Assert.That(
            () =>
            {
                var sut = new IceDecoder(buffer.WrittenMemory, maxCollectionAllocation: allocationLimit);
                _ = sut.DecodeString();
            },
            Throws.Nothing);
    }
}
