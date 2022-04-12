// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

/// <summary>Test encoding and decoding proxies.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class ProxyTests
{
    /// <summary>Provides test case data for <see cref="Decode_proxy(string, string, IProxyFormat, SliceEncoding)"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> DecodeProxyDataSource
    {
        get
        {
            (string, string?, SliceEncoding)[] testData =
            {
                ("icerpc://host:1000/identity?foo=bar", null, SliceEncoding.Slice2),
                ("icerpc://host:1000/identity?foo=bar", null, SliceEncoding.Slice1),
                ("ice://host:10000/identity?transport=tcp", null, SliceEncoding.Slice2),
                ("ice://host:10000/identity?transport=tcp", null, SliceEncoding.Slice1),
                ("ice://opaque/identity?e=1.1&t=1&transport=opaque&v=CTEyNy4wLjAuMeouAAAQJwAAAA==",
                    "ice://opaque/identity?e=1.1&t=1&transport=opaque&v=CTEyNy4wLjAuMeouAAAQJwAAAA==",
                    SliceEncoding.Slice2),
                ("ice://opaque/identity?e=1.1&t=1&transport=opaque&v=CTEyNy4wLjAuMeouAAAQJwAAAA==",
                    "ice://127.0.0.1:12010/identity?transport=tcp&t=10000",
                    SliceEncoding.Slice1)
            };
            foreach ((
                string value,
                string? expected,
                SliceEncoding encoding) in testData)
            {
                yield return new TestCaseData(Proxy.Parse(value), Proxy.Parse(expected ?? value), encoding);
            }
        }
    }

    /// <summary>Verifies that nullable proxies are correctly encoded with both Slice1 and Slice2 encoding.</summary>
    /// <param name="value"></param>
    /// <param name="encoding"></param>
    [Test]
    public void Decode_nullable_proxy(
        [Values("icerpc://host.zeroc.com/hello", null)] string? value,
        [Values(SliceEncoding.Slice1, SliceEncoding.Slice2)] SliceEncoding encoding)
    {
        Proxy? expected = value == null ? null : Proxy.Parse(value);
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, encoding);
        BitSequenceWriter bitSequenceWritter = encoder.GetBitSequenceWriter(1);
        encoder.EncodeNullableProxy(ref bitSequenceWritter, expected);
        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);
        BitSequenceReader bitsequenceReader = decoder.GetBitSequenceReader(1);

        Proxy? decoded = decoder.DecodeNullableProxy(ref bitsequenceReader);

        Assert.That(decoded, Is.EqualTo(expected));
    }

    /// <summary>Verifies that calling <see cref="SliceDecoder.DecodeProxy"/> correctly decodes a proxy.</summary>
    /// <param name="value">The proxy to encode.</param>
    /// <param name="expected">The expected proxy string.</param>
    /// <param name="encoding">The encoding used to decode the proxy.</param>
    [Test, TestCaseSource(nameof(DecodeProxyDataSource))]
    public void Decode_proxy(Proxy value, Proxy expected, SliceEncoding encoding)
    {
        var bufferWriter = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(bufferWriter, encoding);
        encoder.EncodeProxy(value);
        var sut = new SliceDecoder(bufferWriter.WrittenMemory, encoding: encoding);

        Proxy decoded = sut.DecodeProxy();

        Assert.That(decoded, Is.EqualTo(expected));
    }
}
