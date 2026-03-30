// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice.Operations;
using IceRpc.Tests.Common;
using NUnit.Framework;
using ZeroC.Slice.Codec;
using ZeroC.Tests.Common;

namespace IceRpc.Slice.Generator.Tests;

/// <summary>Test encoding and decoding service addresses and proxies.</summary>
[Parallelizable(scope: ParallelScope.All)]
public partial class ProxyTests
{
    /// <summary>Verifies that calling <see cref="SliceProxySliceDecoderExtensions.DecodeProxy" /> correctly decodes
    /// a proxy. </summary>
    /// <param name="value">The service address of the proxy to encode.</param>
    // cSpell:disable
    [TestCase("icerpc://host:1000/path?foo=bar")]
    [TestCase("ice://host:10000/cat/name?transport=tcp")]
    [TestCase("ice://host:10000/cat/name?transport=foo")]
    [TestCase("ice://host:10000/cat/name?transport=ssl&t=30000&z")]
    [TestCase("ice://host:10000/cat/name?t=infinite")]
    [TestCase("ice://opaque/cat/name?transport=opaque&e=1.1&t=1&v=CTEyNy4wLjAuMeouAAAQJwAAAA==")]
    // cSpell:enable
    public void Decode_proxy(ServiceAddress value)
    {
        // Arrange
        var expected = value;
        var bufferWriter = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(bufferWriter);
        encoder.EncodeServiceAddress(value);
        var sut = new SliceDecoder(bufferWriter.WrittenMemory);

        // Act
        var decoded = sut.DecodePingableProxy();

        // Assert
        Assert.That(decoded.ServiceAddress, Is.EqualTo(expected));
    }

    /// <summary>Verifies that a relative proxy gets the invalid invoker by default.</summary>
    [Test]
    public void Decode_relative_proxy()
    {
        // Act/Assert
        Assert.That(() =>
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter);
            encoder.EncodeServiceAddress(new ServiceAddress { Path = "/foo" });
            var decoder = new SliceDecoder(bufferWriter.WrittenMemory);
            return decoder.DecodePingableProxy().Invoker;
        },
        Is.EqualTo(InvalidInvoker.Instance));
    }

    /// <summary>Verifies that a proxy decoded from an incoming request has the invalid invoker by default.</summary>
    [Test]
    public async Task Proxy_decoded_from_an_incoming_request_has_invalid_invoker()
    {
        // Arrange
        var service = new SendProxyTestService();
        var proxy = new SendProxyTestProxy(new ColocInvoker(service));

        // Act
        await proxy.SendProxyAsync(proxy);

        // Assert
        Assert.That(service.ReceivedProxy, Is.Not.Null);
        Assert.That(service.ReceivedProxy!.Value.Invoker, Is.EqualTo(InvalidInvoker.Instance));
    }

    /// <summary>Verifies that the invoker of a proxy decoded from an incoming request can be set using the Slice
    /// feature.</summary>
    [Test]
    public async Task Proxy_decoded_from_an_incoming_request_can_have_invoker_set_through_a_slice_feature()
    {
        // Arrange
        var service = new SendProxyTestService();
        var router = new Router();
        router.Map<ISendProxyTestService>(service);
        var pipeline = new Pipeline();
        var baseProxy = new SendProxyTestProxy(pipeline);
        router.UseFeature<ISliceFeature>(new SliceFeature(baseProxy: baseProxy));

        var proxy = new SendProxyTestProxy(new ColocInvoker(router));

        // Act
        await proxy.SendProxyAsync(proxy);

        // Assert
        Assert.That(service.ReceivedProxy, Is.Not.Null);
        Assert.That(service.ReceivedProxy!.Value.Invoker, Is.EqualTo(pipeline));
    }

    /// <summary>Verifies that a proxy decoded from an incoming response inherits the caller's invoker.</summary>
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

    [Service]
    private sealed partial class ReceiveProxyTestService : IReceiveProxyTestService
    {
        public ValueTask<ReceiveProxyTestProxy> ReceiveProxyAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) =>
            new(new ReceiveProxyTestProxy(InvalidInvoker.Instance, new Uri("icerpc:/hello")));
    }

    [Service]
    private sealed partial class SendProxyTestService : ISendProxyTestService
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
