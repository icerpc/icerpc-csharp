// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests.Slice;

/// <summary>Test encoding and decoding proxies.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class ProxyTests
{
    /// <summary>Provides test case data for <see cref="Decode_proxy(Proxy, Proxy, SliceEncoding)/> test.
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

    private static IEnumerable<Proxy?> DecodeNullableProxySource
    {
        get
        {
            yield return Proxy.Parse("icerpc://host.zeroc.com/hello");
            yield return null;
        }
    }

    /// <summary>Verifies that nullable proxies are correctly encoded withSlice1 encoding.</summary>
    /// <param name="expected">The nullable proxy to test with.</param>
    [Test, TestCaseSource(nameof(DecodeNullableProxySource))]
    public void Decode_slice1_nullable_proxy(Proxy? expected)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeNullableProxy(expected);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        ServicePrx? decoded = decoder.DecodeNullablePrx<ServicePrx>();

        Assert.That(decoded?.Proxy, Is.EqualTo(expected));
    }

    [Test]
    public async Task Cannot_set_alt_endpoints_when_endpoint_is_empty()
    {
        // Arrange
        // Construct a relative proxy since it will have an empty endpoint.
        await using var connection = new ClientConnection("icerpc://localhost");
        var proxy = Proxy.FromConnection(connection, "/foo");

        // Constructing alternate endpoints.
        Endpoint altEndpoint = Endpoint.FromString("icerpc://localhost:10000?transport=foobar");
        ImmutableList<Endpoint> altEndpoints = ImmutableList<Endpoint>.Empty.Add(altEndpoint);

        // Act/Assert
        Assert.Throws<InvalidOperationException>(() => proxy.AltEndpoints = altEndpoints);
        return;
    }

    [Test]
    public void Cannot_clear_endpoints_when_alt_endpoints_is_not_empty()
    {
        // Arrange
        // Creating a proxy with an endpoint.
        var proxy = new Proxy(new Uri("icerpc://localhost:8080/foo"));

        // Constructing alternate endpoints.
        Endpoint altEndpoint = Endpoint.FromString("icerpc://localhost:10000?transport=foobar");
        ImmutableList<Endpoint> altEndpoints = ImmutableList<Endpoint>.Empty.Add(altEndpoint);

        proxy.AltEndpoints = altEndpoints;

        // Act/Assert
        Assert.Throws<InvalidOperationException>(() => proxy.Endpoint = null);
    }

    [Test]
    public void Invalid_fragment_throws_exception()
    {
        // Arrange
        var proxy = new Proxy(Protocol.IceRpc);

        // Act/Assert
        Assert.Throws<ArgumentException>(() => proxy = proxy with { Fragment = "foo<" });
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

        ServicePrx decoded = sut.DecodePrx<ServicePrx>();

        Assert.That(decoded.Proxy, Is.EqualTo(expected));
    }

    /// <summary>Verifies that a relative proxy gets the decoder connection.</summary>
    [Test]
    public async Task Decode_relative_proxy()
    {
        await using var connection = new ClientConnection("icerpc://localhost");
        Assert.That(() =>
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
            encoder.EncodeProxy(Proxy.FromPath("/foo"));
            var decoder = new SliceDecoder(
                bufferWriter.WrittenMemory,
                encoding: SliceEncoding.Slice2,
                connection: connection);

            return decoder.DecodePrx<ServicePrx>().Proxy.Connection;
        },
        Is.EqualTo(connection));
    }
}
