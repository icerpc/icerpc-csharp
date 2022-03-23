// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Tests;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

/// <summary>Test decoding built-in types with the supported Slice encodings.</summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[TestFixture("1.1")]
[TestFixture("2.0")]
[Parallelizable(scope: ParallelScope.All)]
public class DecodingBuiltInTypesTests
{
    private readonly SliceEncoding _encoding;

    /// <summary>Provides test case data for <see cref="Decoding_string(string, byte[], byte[])"/> test.</summary>
    private static IEnumerable<TestCaseData> StringDecodingDataSource
    {
        get
        {
            (byte[], byte[], String)[] testData =
            {
                (
                    new byte[] {0x00},
                    new byte[] {0x00},
                    ""
                ),
                (
                    new byte[] {0xFF, 0x57, 0x00 ,0x00 , 0x00},
                    new byte[] {0x5D, 0x01},
                    "Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit."
                ),
                (
                    new byte[] { 0x9A },
                    new byte[] { 0x69, 0x02 },
                    "국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다" // Korean
                ),
                (
                    new byte[] { 0x9C },
                    new byte[] { 0x71, 0x02 },
                    "旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ" // Japanese
                ),
                (
                    new byte[] { 0x40 },
                    new byte[] { 0x01, 0x01 },
                    "😁😂😃😄😅😆😉😊😋😌😍😏😒😓😔😖"
                )
            };
            foreach ((byte[] sizeBytes11, byte[] sizeBytes20, String expected) in testData)
            {
                yield return new TestCaseData(sizeBytes11, sizeBytes20, expected);
            }
        }
    }

    // Initializes a local field with the encoding from the fixture. Done for tests that need to make some distinction
    // between the 1.1 and 2.0 encodings.
    public DecodingBuiltInTypesTests(string encoding)
    {
        _encoding = SliceEncoding.FromString(encoding);
    }

    /// <summary>Test the decoding of a fixed size numeric type.</summary>
    /// <param name="encodedBytes">The encoded hexadecimal representation of a long to decode.</param>
    /// <param name="expected">The expected long to be decoded.</param>
    [TestCase(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 }, long.MinValue)]
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }, long.MaxValue)]
    public void Decoding_long(byte[] encodedBytes, long expected)
    {
        var decoder = new SliceDecoder(encodedBytes, _encoding);

        long r1 = decoder.DecodeLong();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(sizeof(long)));
    }

    /// <summary>Test the decoding of a variable size numeric type.</summary>
    /// <param name="encodedBytes">The encoded hexadecimal representation of a varlong to decode.</param>
    /// <param name="expected">The expected ulong to be decoded.</param>
    [TestCase(new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 }, SliceEncoder.VarLongMinValue)]
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }, SliceEncoder.VarLongMaxValue)]
    public void Decoding_var_long(byte[] encodedBytes, long expected)
    {
        var decoder = new SliceDecoder(encodedBytes, _encoding);

        long r1 = decoder.DecodeVarLong();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(sizeof(long)));
    }

    /// <summary>Test the decoding of a variable size numeric type.</summary>
    /// <param name="encodedBytes">The encoded hexadecimal representation of a ulong to decode.</param>
    /// <param name="expected">The expected ulong to be decoded.</param>
    [TestCase(new byte[] { 0x00 }, SliceEncoder.VarULongMinValue)]
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, SliceEncoder.VarULongMaxValue)]
    public void Decoding_var_u_long(byte[] encodedBytes, ulong expected)
    {
        var decoder = new SliceDecoder(encodedBytes, _encoding);

        ulong r1 = decoder.DecodeVarULong();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(encodedBytes.Length));
    }

    /// <summary>Test the decoding of a string.</summary>
    /// <param name="sizeBytes11">The hexadecimal representation of expected encoded size if using 1.1 encoding.</param>
    /// <param name="sizeBytes20">The hexadecimal representation of expected encoded size if using 2.0 encoding.</param>
    /// <param name="expected">The expected value of the decoded string.</param>
    [Test, TestCaseSource(nameof(StringDecodingDataSource))]
    public void Decoding_string(byte[] sizeBytes11, byte[] sizeBytes20, String expected)
    {
        var sizeBytes = _encoding.ToString() == "1.1" ? sizeBytes11 : sizeBytes20;
        var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(expected);
        var encodedBytes = sizeBytes.Concat(utf8Bytes).ToArray();
        var decoder = new SliceDecoder(encodedBytes, _encoding);

        string r1 = decoder.DecodeString();

        Assert.That(r1, Is.EqualTo(expected));
    }
}
