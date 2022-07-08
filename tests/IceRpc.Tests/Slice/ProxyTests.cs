// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice;

/// <summary>Test encoding and decoding proxies.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class ProxyTests
{
    /// <summary>Provides test case data for <see cref="Decode_proxy(ServiceAddress, ServiceAddress, SliceEncoding)/> test.
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
                yield return new TestCaseData(ServiceAddress.Parse(value), ServiceAddress.Parse(expected ?? value), encoding);
            }
        }
    }

    private static IEnumerable<ServiceAddress?> DecodeNullableProxySource
    {
        get
        {
            yield return ServiceAddress.Parse("icerpc://host.zeroc.com/hello");
            yield return null;
        }
    }

    /// <summary>Verifies that nullable proxies are correctly encoded withSlice1 encoding.</summary>
    /// <param name="expected">The nullable proxy to test with.</param>
    [Test, TestCaseSource(nameof(DecodeNullableProxySource))]
    public void Decode_slice1_nullable_proxy(ServiceAddress? expected)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeNullableServiceAddress(expected);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        ServiceProxy? decoded = decoder.DecodeNullableProxy<ServiceProxy>();

        Assert.That(decoded?.ServiceAddress, Is.EqualTo(expected));
    }

    /// <summary>Verifies that calling <see cref="SliceDecoder.DecodeProxy"/> correctly decodes a proxy.</summary>
    /// <param name="value">The service address of the proxy to encode.</param>
    /// <param name="expected">The expected URI string of the service address.</param>
    /// <param name="encoding">The encoding used to decode the service address.</param>
    [Test, TestCaseSource(nameof(DecodeProxyDataSource))]
    public void Decode_proxy(ServiceAddress value, ServiceAddress expected, SliceEncoding encoding)
    {
        var bufferWriter = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(bufferWriter, encoding);
        encoder.EncodeServiceAddress(value);
        var sut = new SliceDecoder(bufferWriter.WrittenMemory, encoding: encoding);

        ServiceProxy decoded = sut.DecodeProxy<ServiceProxy>();

        Assert.That(decoded.ServiceAddress, Is.EqualTo(expected));
    }

    /// <summary>Verifies that a relative proxy gets the decoder's connection as invoker.</summary>
    [Test]
    public async Task Decode_relative_proxy()
    {
        await using var connection = new ClientConnection("icerpc://localhost");
        Assert.That(() =>
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
            encoder.EncodeServiceAddress(new ServiceAddress { Path = "/foo" });
            var decoder = new SliceDecoder(
                bufferWriter.WrittenMemory,
                encoding: SliceEncoding.Slice2,
                relativeProxyInvoker: connection);

            return decoder.DecodeProxy<ServiceProxy>().Invoker;
        },
        Is.EqualTo(connection));
    }

    [Test]
    public async Task Downcast_proxy_with_as_sync_succeeds()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyDerivedInterface())
            .BuildServiceProvider(validateScopes: true);

        var proxy = new MyBaseInterfaceProxy(provider.GetRequiredService<ClientConnection>());
        provider.GetRequiredService<Server>().Listen();

        MyDerivedInterfaceProxy? derived = await proxy.AsAsync<MyDerivedInterfaceProxy>();

        Assert.That(derived, Is.Not.Null);
    }

    [Test]
    public async Task Downcast_proxy_with_as_aync_fails()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyBaseInterface())
            .BuildServiceProvider(validateScopes: true);

        var proxy = new MyBaseInterfaceProxy(provider.GetRequiredService<ClientConnection>());
        provider.GetRequiredService<Server>().Listen();

        MyDerivedInterfaceProxy? derived = await proxy.AsAsync<MyDerivedInterfaceProxy>();

        Assert.That(derived, Is.Null);
    }

    private class MyBaseInterface : Service, IMyBaseInterface
    {
    }

    private class MyDerivedInterface : MyBaseInterface, IMyDerivedInterface
    {
    }
}
