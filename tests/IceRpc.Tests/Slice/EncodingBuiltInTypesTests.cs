// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;
namespace IceRpc.Slice.Tests;

/// <summary>Test encoding of built-in types with the supported Slice encodings.</summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[TestFixture("1.1")]
[TestFixture("2.0")]
[Parallelizable(scope: ParallelScope.All)]
public class EncodingBuiltInTypesTests
{
    private readonly SliceEncoding _encoding;

    /// <summary>Provides test case data for <see cref="Encoding_string(string, byte[], byte[])"/> test.</summary>
    private static IEnumerable<TestCaseData> StringEncodingDataSource
    {
        get
        {
            (String, byte[], byte[])[] testData =
            {
                (
                    "Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit.",
                    new byte[] {0xFF, 0x57, 0x00 ,0x00 , 0x00},
                    new byte[] {0x5D, 0x01}
                ),
                (
                    "국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다", // Korean
                    new byte[] { 0x9A },
                    new byte[] { 0x69, 0x02 }
                ),
                (
                    "旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ", // Japanese
                    new byte[] { 0x9C },
                    new byte[] { 0x71, 0x02 }
                ),
                (
                    "😁😂😃😄😅😆😉😊😋😌😍😏😒😓😔😖",
                    new byte[] { 0x40 },
                    new byte[] { 0x01, 0x01 }
                )
            };
            foreach ((String testString, byte[] sizeBytes11, byte[] sizeBytes20) in testData)
            {
                yield return new TestCaseData(testString, sizeBytes11, sizeBytes20);
            }
        }
    }

    // Initializes a local field with the encoding from the fixture. Done for tests that need to make some distinction
    // between the 1.1 and 2.0 encodings.
    public EncodingBuiltInTypesTests(string encoding)
    {
        _encoding = SliceEncoding.FromString(encoding);
    }

    /// <summary>Test the encoding of a fixed size numeric type.</summary>
    /// <param name="p1">The value to be encoded.</param>
    /// <param name="expected">The expected hexadecimal representation of the encoded value.</param>
    [TestCase(long.MinValue, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 })]
    [TestCase(long.MaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F })]
    public void Encoding_long(long p1, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, _encoding);

        encoder.EncodeLong(p1);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(sizeof(long)));
        Assert.That(bufferWriter.WrittenMemory.Length, Is.EqualTo(sizeof(long)));
        Assert.That(new ArraySegment<Byte>(buffer, 0, sizeof(long)), Is.EqualTo(expected));
    }

    /// <summary>Test the encoding of a variable size numeric type.</summary>
    /// <param name="p1">The value to be encoded.</param>
    /// <param name="expected">The expected hexadecimal representation of the encoded value.</param>
    [TestCase(SliceEncoder.VarLongMinValue, new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 })]
    [TestCase(SliceEncoder.VarLongMaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F })]
    public void Encoding_var_long(long p1, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, _encoding);

        encoder.EncodeVarLong(p1);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(sizeof(long)));
        Assert.That(bufferWriter.WrittenMemory.Length, Is.EqualTo(sizeof(long)));
        Assert.That(new ArraySegment<Byte>(buffer, 0, sizeof(long)), Is.EqualTo(expected));
    }

    /// <summary>Verifies that <see cref="SliceEncoder.EncodeVarLong"/> will throw an ArgumentOutOfRangeException
    /// if the parameter is larger than the max value of a varlong or smaller than the min value of a varlong.</summary>
    /// <param name="p1">The value to be encoded.</param>
    /// <param name="expected">The expected hexadecimal representation of the encoded value.</param>
    [TestCase(SliceEncoder.VarLongMinValue - 1)]
    [TestCase(SliceEncoder.VarLongMaxValue + 1)]
    public void Encoding_var_long_throws_out_of_range(long p1)
    {
        // Due to limitations on ref types, we cannot setup the arrange outside of the assertion. This is a result of
        // being unable to use ref local inside anonymous methods, lambda expressions, or query expressions.
        Assert.That(() =>
        {
            // Arrange
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, _encoding);

            // Act
            encoder.EncodeVarLong(p1);
        }, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    /// <summary>Test the encoding of a variable size unsigned numeric type.</summary>
    /// <param name="p1">The value to be encoded.</param>
    /// <param name="expected">The expected hexadecimal representation of the encoded value.</param>
    [TestCase(SliceEncoder.VarULongMinValue, new byte[] { 0x00 })]
    [TestCase(SliceEncoder.VarULongMaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    public void Encoding_var_u_long(ulong p1, byte[] expected)
    {
        // Arrange
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, _encoding);

        // Act
        encoder.EncodeVarULong(p1);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(expected.Length));
        Assert.That(bufferWriter.WrittenMemory.Length, Is.EqualTo(expected.Length));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    /// <summary>Verifies that <see cref="SliceEncoder.EncodeVarULong"/> will throw an ArgumentOutOfRangeException
    /// if the parameter is larger than the max value of a varulong.</summary>
    /// <param name="p1">The value to be encoded.</param>
    [TestCase(SliceEncoder.VarULongMaxValue + 1)]
    public void Encoding_var_u_long_throws_out_of_range(ulong p1)
    {
        // Due to limitations on ref types, we cannot setup the arrange outside of the assertion. This is a result of
        // being unable to use ref local inside anonymous methods, lambda expressions, or query expressions.
        Assert.That(() =>
        {
            // Arrange
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, _encoding);

            // Act
            encoder.EncodeVarULong(p1);
        }, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    /// <summary>Test the encoding of a non-empty string.</summary>
    /// <param name="p1">The value to be encoded.</param>
    /// <param name="sizeBytes11">The expected hexadecimal representation of the encoded value if using 1.1 encoding.
    /// </param>
    /// <param name="sizeBytes20">The expected hexadecimal representation of the encoded value if using 1.1 encoding.
    /// </param>
    [Test, TestCaseSource(nameof(StringEncodingDataSource))]
    public void Encoding_string(string p1, byte[] sizeBytes11, byte[] sizeBytes20)
    {
        var expectedSizeBytes = _encoding.ToString() == "1.1" ? sizeBytes11 : sizeBytes20;
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, _encoding);

        encoder.EncodeString(p1);

        var writtenBytes = buffer[0..bufferWriter.WrittenMemory.Length];
        var sizeBytes = writtenBytes[0..expectedSizeBytes.Length];
        var utf8Bytes = writtenBytes[expectedSizeBytes.Length..writtenBytes.Length];
        Assert.That(sizeBytes, Is.EqualTo(expectedSizeBytes));
        Assert.That(System.Text.Encoding.UTF8.GetString(utf8Bytes), Is.EqualTo(p1));
    }

    /// <summary>Verifying encoding an empty string uses <see cref="SliceEncoder.EncodeSize"/> with value 0.</summary>
    [Test]
    public void Encoding_empty_string()
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, _encoding);
        var p1 = "";

        encoder.EncodeString(p1);

        var writtenBytes = buffer[0..bufferWriter.WrittenMemory.Length];
        Console.WriteLine(BitConverter.ToString(writtenBytes));
        Assert.That(System.Text.Encoding.UTF8.GetString(writtenBytes), Is.EqualTo("\0"));
    }
}
