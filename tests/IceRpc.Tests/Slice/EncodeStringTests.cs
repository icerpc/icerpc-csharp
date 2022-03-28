// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

/// <summary>Test encoding strings with the supported Slice encodings.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class EncodeStringTests
{
    /// <summary>Tests the encoding of a string. The only difference between encoding strings with the 1.1 encoding and
    /// the 2.0 encoding is how the size gets encoded. Since <see cref="Encoding_size(string, byte[], byte[])"/>
    /// tests the size encoding, this test only needs to verify how strings are encoded with 2.0. </summary>
    /// <param name="value">The string to be encoded.</param>
    [TestCase("")]
    [TestCase("Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit.")]
    [TestCase("국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다")] // Korean
    [TestCase("旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ")]  // Japanese
    [TestCase("😁😂😃😄😅😆😉😊😋😌😍😏😒😓😔😖")]
    public void Encoding_string(string value)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice20);

        encoder.EncodeString(value);

        var utf8ByteCount = System.Text.Encoding.UTF8.GetByteCount(value);
        var writtenBytes = bufferWriter.WrittenMemory.ToArray();
        var utf8Bytes = writtenBytes[(writtenBytes.Length - utf8ByteCount)..];
        Assert.That(encoder.EncodedByteCount, Is.EqualTo(bufferWriter.WrittenMemory.Length));
        Assert.That(value, Is.EqualTo(System.Text.Encoding.UTF8.GetString(utf8Bytes)));
    }
}
