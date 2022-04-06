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
                (
                    "icerpc://host:1000/identity?foo=bar",
                    null,
                    SliceEncoding.Slice2
                ),
                (
                    "icerpc://host:1000/identity?foo=bar",
                    null,
                    SliceEncoding.Slice1
                ),
                (
                    "ice://host:10000/identity?transport=tcp",
                    null,
                    SliceEncoding.Slice2
                ),
                (
                    "ice://host:10000/identity?transport=tcp",
                    null,
                    SliceEncoding.Slice1
                ),
                (
                    "ice://opaque/identity?e=1.1&t=1&transport=opaque&v=CTEyNy4wLjAuMeouAAAQJwAAAA==",
                    "ice://opaque/identity?e=1.1&t=1&transport=opaque&v=CTEyNy4wLjAuMeouAAAQJwAAAA==",
                    SliceEncoding.Slice2
                ),
                (
                    "ice://opaque/identity?e=1.1&t=1&transport=opaque&v=CTEyNy4wLjAuMeouAAAQJwAAAA==",
                    "ice://127.0.0.1:12010/identity?transport=tcp&t=10000",
                    SliceEncoding.Slice1
                )
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
