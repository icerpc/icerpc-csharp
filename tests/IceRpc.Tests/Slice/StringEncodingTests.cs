// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Tests;
using NUnit.Framework;
using System.IO.Pipelines;
using System.Buffers;
namespace IceRpc.Slice.Tests;

/// <summary>Test encoding strings with the supported Slice encodings.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class StringEncodingTests
{
    /// <summary>Tests the encoding of a string. The only difference between encoding strings with the 1.1 encoding and
    /// the 2.0 encoding is how the size gets encoded. Since <see cref="Encoding_size(string, byte[], byte[])"/>
    /// tests the size encoding, this test only needs to verify how strings are encoded with 2.0. </summary>
    /// <param name="value">The string to be encoded.</param>
    [TestCase("")]
    [TestCase("국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다")] // Korean
    [TestCase("旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ")]  // Japanese
    [TestCase("😁😂😃😄😅😆😉😊😋😌😍😏😒😓😔😖")]
    public void Encode_single_segment_string(string value)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        encoder.EncodeString(value);

        var utf8ByteCount = System.Text.Encoding.UTF8.GetByteCount(value);
        var writtenBytes = bufferWriter.WrittenMemory.ToArray();
        var utf8Bytes = writtenBytes[(writtenBytes.Length - utf8ByteCount)..];
        Assert.That(encoder.EncodedByteCount, Is.EqualTo(bufferWriter.WrittenMemory.Length));
        Assert.That(value, Is.EqualTo(System.Text.Encoding.UTF8.GetString(utf8Bytes)));
    }

    /// <summary>Tests the encoding of a string using a custom memory pool.</summary>
    /// <param name="value">The string to be encoded.</param>
    [TestCase("Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit.")]
    [TestCase("국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다")] // Korean
    [TestCase("旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ")]  // Japanese
    [TestCase("😁😂😃😄😅😆😉😊😋😌😍😏😒😓😔😖")]
    public void Encode_multi_segment_string(string value)
    {
        // Arrange
        // now with a custom memory pool with a tiny max buffer size
        using var customPool = new TestMemoryPool(7);

        // minimumSegmentSize is not the same as the sizeHint given to GetMemory/GetSpan; it refers to the
        // minBufferSize given to Rent
        var pipe = new Pipe(new PipeOptions(pool: customPool, minimumSegmentSize: 5));
        var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);

        // Act
        encoder.EncodeString(value);
        pipe.Writer.Complete();

        // Assert
        pipe.Reader.TryRead(out ReadResult readResult);
        byte[] writtenBytes = readResult.Buffer.ToArray();
        var utf8ByteCount = System.Text.Encoding.UTF8.GetByteCount(value);
        var writtenUtf8Bytes = writtenBytes[(writtenBytes.Length - utf8ByteCount)..];

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(readResult.Buffer.Length));
        Assert.That(value, Is.EqualTo(System.Text.Encoding.UTF8.GetString(writtenUtf8Bytes)));
    }
}
