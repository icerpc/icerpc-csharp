// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Ice;
using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.IO.Pipelines;

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
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeNullableServiceAddress(expected);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        // Act
        PingableProxy? decoded = decoder.DecodeNullableProxy<PingableProxy>();

        // Assert
        Assert.That(decoded?.ServiceAddress, Is.EqualTo(expected));
    }

    /// <summary>Verifies that calling <see cref="SliceDecoder.DecodeProxy" /> correctly decodes a proxy.</summary>
    /// <param name="value">The service address of the proxy to encode.</param>
    /// <param name="expected">The expected URI string of the service address.</param>
    /// <param name="encoding">The encoding used to decode the service address.</param>
    [Test, TestCaseSource(nameof(DecodeProxyDataSource))]
    public void Decode_proxy(ServiceAddress value, ServiceAddress expected, SliceEncoding encoding)
    {
        // Arrange
        var bufferWriter = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(bufferWriter, encoding);
        encoder.EncodeServiceAddress(value);
        var sut = new SliceDecoder(bufferWriter.WrittenMemory, encoding: encoding);

        // Act
        var decoded = sut.DecodeProxy<GenericProxy>();

        // Assert
        Assert.That(decoded.ServiceAddress, Is.EqualTo(expected));
    }

    /// <summary>Verifies that a relative proxy decoded with the default service proxy factory gets a null invoker.
    /// </summary>
    [Test]
    public void Decode_relative_proxy()
    {
        // Act/Assert
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
        // Arrange
        var proxy = new MyBaseInterfaceProxy(new ColocInvoker(new MyDerivedInterfaceService()));

        // Act
        MyDerivedInterfaceProxy? derived = await proxy.AsAsync<MyDerivedInterfaceProxy>();

        // Assert
        Assert.That(derived, Is.Not.Null);
    }

    [Test]
    public async Task Downcast_proxy_with_as_async_fails()
    {
        // Arrange
        var proxy = new MyBaseInterfaceProxy(new ColocInvoker(new MyBaseInterfaceService()));

        // Act
        MyDerivedInterfaceProxy? derived = await proxy.AsAsync<MyDerivedInterfaceProxy>();

        // Assert
        Assert.That(derived, Is.Null);
    }

    /// <summary>Verifies that a proxy decoded from an incoming request has a null invoker by default.</summary>
    [Test]
    public async Task Proxy_decoded_from_an_incoming_request_has_null_invoker()
    {
        // Arrange
        var service = new SendProxyTestService();
        var proxy = new SendProxyTestProxy(new ColocInvoker(service));

        // Act
        await proxy.SendProxyAsync(proxy);

        // Assert
        Assert.That(service.ReceivedProxy, Is.Not.Null);
        Assert.That(service.ReceivedProxy!.Value.Invoker, Is.Null);
    }

    /// <summary>Verifies that the invoker of a proxy decoded from an incoming request can be set using a Slice
    /// feature.</summary>
    [Test]
    public async Task Proxy_decoded_from_an_incoming_request_can_have_invoker_set_through_a_slice_feature()
    {
        // Arrange
        var service = new SendProxyTestService();
        var pipeline = new Pipeline();
        var router = new Router();
        router.Map<ISendProxyTestService>(service);
        router.UseFeature<ISliceFeature>(
            new SliceFeature(proxyFactory: (serviceAddress, _) =>
                new GenericProxy
                {
                    Invoker = pipeline,
                    ServiceAddress = serviceAddress
                }));

        var proxy = new SendProxyTestProxy(new ColocInvoker(router));

        // Act
        await proxy.SendProxyAsync(proxy);

        // Assert
        Assert.That(service.ReceivedProxy, Is.Not.Null);
        Assert.That(service.ReceivedProxy!.Value.Invoker, Is.EqualTo(pipeline));
    }

    /// <summary>Verifies that a proxy decoded from an incoming response inherits the callers invoker.</summary>
    [Test]
    public async Task Proxy_decoded_from_an_incoming_response_inherits_the_callers_invoker()
    {
        // Arrange
        IInvoker invoker = new ColocInvoker(new ReceiveProxyTestService());
        var proxy = new ReceiveProxyTestProxy(invoker);

        // Act
        ReceiveProxyTestProxy received = await proxy.ReceiveProxyAsync();

        // Assert
        Assert.That(received.Invoker, Is.EqualTo(invoker));
    }

    private class MyBaseInterfaceService : Service, IMyBaseInterfaceService
    {
    }

    private sealed class MyDerivedInterfaceService : MyBaseInterfaceService, IMyDerivedInterfaceService
    {
    }

    private sealed class ReceiveProxyTestService : Service, IReceiveProxyTestService
    {
        public ValueTask<ReceiveProxyTestProxy> ReceiveProxyAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) =>
            new(new ReceiveProxyTestProxy { ServiceAddress = new(new Uri("icerpc:/hello")) });
    }

    private sealed class SendProxyTestService : Service, ISendProxyTestService
    {
        public SendProxyTestProxy? ReceivedProxy { get; private set; }

        public ValueTask SendProxyAsync(
            SendProxyTestProxy proxy,
            IFeatureCollection features,
            CancellationToken cancellationToken = default)
        {
            ReceivedProxy = proxy;
            return default;
        }
    }
}
