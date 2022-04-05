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
            (string, string, IProxyFormat format, SliceEncoding)[] testData =
            {
                (
                    "icerpc://host:1000/identity?foo=bar",
                    "icerpc://host:1000/identity?foo=bar",
                    UriProxyFormat.Instance,
                    SliceEncoding.Slice20
                ),
                (
                    "identity:tcp -h host -p 10000",
                    "identity:tcp -h host -p 10000",
                    IceProxyFormat.Default,
                    SliceEncoding.Slice11
                ),
                (
                    "identity:opaque -t 1 -e 1.1 -v CTEyNy4wLjAuMeouAAAQJwAAAA==",
                    "identity:tcp -h 127.0.0.1 -p 12010 -t 10000",
                    IceProxyFormat.Default,
                    SliceEncoding.Slice11
                ),
                (
                    "identity:opaque -t 1 -e 1.1 -v CTEyNy4wLjAuMeouAAAQJwAAAA==",
                    "identity:opaque -t 1 -e 1.1 -v CTEyNy4wLjAuMeouAAAQJwAAAA==",
                    IceProxyFormat.Default,
                    SliceEncoding.Slice20
                ),
                (
                    "identity:opaque -t 99 -e 1.1 -v abcd", // 99 = unknown and -t -e -v in this order
                    "identity:opaque -t 99 -v abcd",
                    IceProxyFormat.Default,
                    SliceEncoding.Slice11
                ),
                (
                    "identity:opaque -t 99 -e 1.1 -v abcd", // 99 = unknown and -t -e -v in this order
                    "identity:opaque -t 99 -e 1.1 -v abcd",
                    IceProxyFormat.Default,
                    SliceEncoding.Slice20
                )
            };
            foreach ((
                string value,
                string expected,
                IProxyFormat format,
                SliceEncoding encoding) in testData)
            {
                yield return new TestCaseData(value, expected, format, encoding);
            }
        }
    }

    /// <summary>Verifies that calling <see cref="SliceDecoder.DecodeProxy"/> correctly decodes a proxy.</summary>
    /// <param name="value">The proxy to encode</param>
    /// <param name="expected">The expected decoded </param>
    /// <param name="format">The proxy string format</param>
    /// <param name="encoding">The encoding used to decode the proxy.</param>
    [Test, TestCaseSource(nameof(DecodeProxyDataSource))]
    public void Decode_proxy(string value, string expected, IProxyFormat format, SliceEncoding encoding)
    {
        byte[] buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, encoding);
        Proxy proxy = format.Parse(value, Proxy.DefaultInvoker);
        encoder.EncodeProxy(proxy);

        var sut = new SliceDecoder(buffer, encoding: encoding);

        Proxy decoded = sut.DecodeProxy();

        Proxy expectedProxy = format.Parse(expected, Proxy.DefaultInvoker);
        Assert.That(decoded, Is.EqualTo(expectedProxy));
    }
}
