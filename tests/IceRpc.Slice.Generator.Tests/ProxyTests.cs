// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice.Operations;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    [TestCase("icerpc://host:1000/path?transport=tcp")]
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

    /// <summary>Verifies that a relative proxy is encoded as its path.</summary>
    [Test]
    public void Encode_relative_proxy()
    {
        // Arrange
        var bufferWriter = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(bufferWriter);

        // Act
        encoder.EncodePingableProxy(PingableProxy.FromPath("/foo"));

        // Assert
        var decoder = new SliceDecoder(bufferWriter.WrittenMemory);
        Assert.That(decoder.DecodeString(), Is.EqualTo("/foo"));
    }

    /// <summary>Verifies that a relative proxy decoded without a base proxy remains relative and gets the invalid
    /// invoker.</summary>
    [Test]
    public void Decode_relative_proxy_without_base_proxy()
    {
        // Arrange
        var bufferWriter = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(bufferWriter);
        encoder.EncodeString("/foo");
        var sut = new SliceDecoder(bufferWriter.WrittenMemory);

        // Act
        PingableProxy decoded = sut.DecodePingableProxy();

        // Assert
        Assert.That(decoded.IsRelative, Is.True);
        Assert.That(decoded.ServiceAddress.Path, Is.EqualTo("/foo"));
        Assert.That(decoded.Invoker, Is.EqualTo(InvalidInvoker.Instance));
    }

    /// <summary>Verifies that a relative proxy decoded with a base proxy is resolved against the service address of
    /// this base proxy.</summary>
    [Test]
    public void Decode_relative_proxy_with_base_proxy()
    {
        // Arrange
        var pipeline = new Pipeline();
        var baseProxy = new PingableProxy(pipeline, new Uri("icerpc://host:1000/base?transport=tcp"));
        var bufferWriter = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(bufferWriter);
        encoder.EncodeString("/foo");
        var sut = new SliceDecoder(bufferWriter.WrittenMemory, decodingContext: baseProxy);

        // Act
        PingableProxy decoded = sut.DecodePingableProxy();

        // Assert
        Assert.That(decoded.IsRelative, Is.False);
        Assert.That(decoded.ServiceAddress, Is.EqualTo(baseProxy.ServiceAddress.WithPath("/foo")));
        Assert.That(decoded.Invoker, Is.EqualTo(pipeline));
    }

    [Test]
    public void Slice_feature_rejects_relative_base_proxy() =>
        Assert.That(() => new SliceFeature(baseProxy: PingableProxy.FromPath("/base")), Throws.ArgumentException);

    /// <summary>Verifies that decoding a relative proxy whose path is invalid for the base proxy's protocol throws
    /// <see cref="InvalidDataException" />.</summary>
    [Test]
    public void Decode_relative_proxy_with_invalid_path_for_base_proxy_fails()
    {
        // Arrange
        var baseProxy = new PingableProxy(InvalidInvoker.Instance, new Uri("ice://host:1000/base"));
        var bufferWriter = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(bufferWriter);
        encoder.EncodeString("/a/b/c"); // too many slashes for an ice path

        // Act/Assert
        Assert.That(
            () =>
            {
                var decoder = new SliceDecoder(bufferWriter.WrittenMemory, decodingContext: baseProxy);
                return decoder.DecodePingableProxy();
            },
            Throws.InstanceOf<InvalidDataException>());
    }

    /// <summary>Verifies that a relative proxy is immutable.</summary>
    [Test]
    public void Relative_proxy_cannot_be_modified()
    {
        PingableProxy proxy = PingableProxy.FromPath("/foo");

        Assert.That(
            () => proxy with { ServiceAddress = new ServiceAddress.Ice() },
            Throws.InvalidOperationException);
        Assert.That(() => proxy with { Invoker = new Pipeline() }, Throws.InvalidOperationException);
        Assert.That(() => proxy with { EncodeOptions = new SliceEncodeOptions() }, Throws.InvalidOperationException);
    }

    [Test]
    public void Invoke_with_relative_proxy_fails() =>
        Assert.That(() => PingableProxy.FromPath("/foo").PingAsync(), Throws.InvalidOperationException);

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
        router.Map(service);
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
