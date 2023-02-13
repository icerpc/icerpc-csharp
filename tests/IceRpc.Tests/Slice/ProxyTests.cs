// Copyright (c) ZeroC, Inc.

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
    /// <summary>Provides test case data for <see cref="Decode_proxy(ServiceAddress, ServiceAddress, SliceEncoding)" />
    /// test.</summary>
    private static IEnumerable<TestCaseData> DecodeProxyDataSource
    {
        get
        {
            (string, string?, SliceEncoding)[] testData =
            {
                // cSpell:disable
                ("icerpc://host:1000/identity?foo=bar", null, SliceEncoding.Slice2),
                ("icerpc://host:1000/identity?foo=bar", null, SliceEncoding.Slice1),
                ("ice://host:10000/identity?transport=tcp", null, SliceEncoding.Slice2),
                ("ice://host:10000/identity?transport=tcp", null, SliceEncoding.Slice1),
                ("ice://opaque/identity?e=1.1&t=1&transport=opaque&v=CTEyNy4wLjAuMeouAAAQJwAAAA==",
                    "ice://opaque/identity?e=1.1&t=1&transport=opaque&v=CTEyNy4wLjAuMeouAAAQJwAAAA==",
                    SliceEncoding.Slice2),
                ("ice://opaque/identity?e=1.1&t=1&transport=opaque&v=CTEyNy4wLjAuMeouAAAQJwAAAA==",
                    "ice://127.0.0.1:12010/identity?transport=tcp&t=10000",
                    SliceEncoding.Slice1),
                ("ice:/path?adapter-id=foo", null, SliceEncoding.Slice1),
                ("ice:/path", null, SliceEncoding.Slice1)
                // cSpell:enable
            };
            foreach ((
                string value,
                string? expected,
                SliceEncoding encoding) in testData)
            {
                yield return new TestCaseData(
                    new ServiceAddress(new Uri(value)),
                    new ServiceAddress(new Uri(expected ?? value)),
                    encoding);
            }
        }
    }

    private static IEnumerable<ServiceAddress?> DecodeNullableProxySource
    {
        get
        {
            yield return new ServiceAddress(new Uri("icerpc://host.zeroc.com/hello"));
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

        PingableProxy? decoded = decoder.DecodeNullableProxy<PingableProxy>();

        Assert.That(decoded?.ServiceAddress, Is.EqualTo(expected));
    }

    /// <summary>Verifies that calling <see cref="SliceDecoder.DecodeProxy" /> correctly decodes a proxy.</summary>
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

        var decoded = sut.DecodeProxy<GenericProxy>();

        Assert.That(decoded.ServiceAddress, Is.EqualTo(expected));
    }

    /// <summary>Verifies that a relative proxy decoded with the default service proxy factory gets a null invoker.
    /// </summary>
    [Test]
    public void Decode_relative_proxy()
    {
        Assert.That(() =>
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
            encoder.EncodeServiceAddress(new ServiceAddress { Path = "/foo" });
            var decoder = new SliceDecoder(
                bufferWriter.WrittenMemory,
                encoding: SliceEncoding.Slice2);

            return decoder.DecodeProxy<GenericProxy>().Invoker;
        },
        Is.Null);
    }

    [Test]
    public async Task Downcast_proxy_with_as_async_succeeds()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddClientServerColocTest(dispatcher: new MyDerivedInterfaceService())
            .BuildServiceProvider(validateScopes: true);

        var proxy = new MyBaseInterfaceProxy(provider.GetRequiredService<ClientConnection>());
        provider.GetRequiredService<Server>().Listen();

        MyDerivedInterfaceProxy? derived = await proxy.AsAsync<MyDerivedInterfaceProxy>();

        Assert.That(derived, Is.Not.Null);
    }

    [Test]
    public async Task Downcast_proxy_with_as_async_fails()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddClientServerColocTest(dispatcher: new MyBaseInterfaceService())
            .BuildServiceProvider(validateScopes: true);

        var proxy = new MyBaseInterfaceProxy(provider.GetRequiredService<ClientConnection>());
        provider.GetRequiredService<Server>().Listen();

        MyDerivedInterfaceProxy? derived = await proxy.AsAsync<MyDerivedInterfaceProxy>();

        Assert.That(derived, Is.Null);
    }

    private class MyBaseInterfaceService : Service, IMyBaseInterfaceService
    {
    }

    private sealed class MyDerivedInterfaceService : MyBaseInterfaceService, IMyDerivedInterfaceService
    {
    }
}
