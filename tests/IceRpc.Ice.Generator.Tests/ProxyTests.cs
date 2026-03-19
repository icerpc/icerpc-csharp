// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using IceRpc.Tests.Common;
using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Generator.Tests;

/// <summary>Test encoding and decoding proxies generated from .ice definitions.</summary>
[Parallelizable(scope: ParallelScope.All)]
public partial class ProxyTests
{
    [Test]
    public async Task Create_proxy_with_null_protocol_fails()
    {
        // Arrange
        var serviceAddress = new ServiceAddress(protocol: null) { Path = "/foo" };

        // Act & Assert
        Assert.That(() => new PingableProxy(InvalidInvoker.Instance, serviceAddress),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public async Task Proxy_has_default_service_path_with_ice_protocol()
    {
        // Arrange
        var proxy = new PingableProxy() { Invoker = InvalidInvoker.Instance };

        // Assert
        Assert.That(proxy.ServiceAddress.Path, Is.EqualTo(PingableProxy.DefaultServicePath));
        Assert.That(proxy.ServiceAddress.Protocol, Is.EqualTo(Protocol.Ice));
    }

    /// <summary>Verifies that calling DecodeProxy correctly decodes a proxy.</summary>
    /// <param name="value">The service address of the proxy to encode.</param>
    /// <param name="expected">The expected URI string of the service address.</param>
    // cSpell:disable
    [TestCase("icerpc://host:1000/path?foo=bar", null)]
    [TestCase("ice://host:10000/cat/name?transport=tcp", null)]
    [TestCase("ice://host:10000/cat/name?transport=foo", null)]
    [TestCase("ice://host:10000/cat/name?transport=ssl&t=30000&z", null)]
    [TestCase(
        "ice://host:10000/cat/name?t=infinite",
        "ice://host:10000/cat/name?t=-1&transport=tcp")]
    [TestCase(
        "ice://opaque/cat/name?transport=opaque&e=1.1&t=1&v=CTEyNy4wLjAuMeouAAAQJwAAAA==",
        "ice://127.0.0.1:12010/cat/name?transport=tcp&t=10000")]
    [TestCase(
        "ice://opaque/cat/name?transport=opaque&e=1.0&t=1&v=CTEyNy4wLjAuMeouAAAQJwAAAA==",
        "ice://127.0.0.1:12010/cat/name?transport=tcp&t=10000")]
    [TestCase("ice://opaque/cat/name?transport=opaque&t=99&v=1234", null)]
    [TestCase("ice://opaque/cat/name?transport=opaque&e=1.0&t=99&v=1234", null)]
    [TestCase("ice:/cat/name?adapter-id=foo", null)]
    [TestCase("ice:/cat/name", null)]
    // cSpell:enable
    public void Decode_proxy(ServiceAddress value, ServiceAddress? expected)
    {
        // Arrange
        expected ??= value;
        var bufferWriter = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(bufferWriter);
        encoder.EncodeIceObjectProxy(new IceObjectProxy(InvalidInvoker.Instance, value));
        var sut = new IceDecoder(bufferWriter.WrittenMemory);

        // Act
        var decoded = sut.DecodePingableProxy();

        // Assert
        Assert.That(decoded?.ServiceAddress, Is.EqualTo(expected));
    }

    /// <summary>Verifies that proxies are correctly encoded.</summary>
    /// <param name="expected">The proxy to test with.</param>
    [TestCase("icerpc://host.zeroc.com/hello")]
    [TestCase(null)]
    public void Decode_proxy(ServiceAddress? expected)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeIceObjectProxy(
            expected is not null ? new IceObjectProxy(InvalidInvoker.Instance, expected) : null);
        var decoder = new IceDecoder(buffer.WrittenMemory);

        // Act
        AnotherPingableProxy? decoded = decoder.DecodeAnotherPingableProxy();

        // Assert
        Assert.That(decoded?.ServiceAddress, Is.EqualTo(expected));
    }

    [TestCase("ice://host/path?transport=opaque&t=-10")] // invalid value for t
    [TestCase("ice://host/path?transport=opaque&t=abc")] // invalid value for t
    [TestCase("ice://host/path?transport=opaque&v=1234")] // no t
    [TestCase("ice://host/path?transport=opaque&t=1&v=%1234")] // invalid value for v
    [TestCase("ice://host/path?transport=opaque&t=1")] // no v
    [TestCase("ice://host/path?transport=opaque&t=1&v=1234&foo=bar")] // unknown param
    [TestCase("ice://host/path?transport=opaque&e=2.0&t=1&v=1234")] // bad e
    public void Encode_invalid_opaque_proxy_fails(ServiceAddress serviceAddress) =>
        Assert.That(() =>
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new IceEncoder(bufferWriter);
            encoder.EncodeIceObjectProxy(new IceObjectProxy(InvalidInvoker.Instance, serviceAddress));
        },
        Throws.TypeOf<FormatException>());

    // we have to use icerpc since these paths are not valid for ice
    [TestCase("icerpc://host:10000")]
    [TestCase("icerpc://host:10000/foo/")]
    public void Encode_proxy_with_null_identity_fails(ServiceAddress serviceAddress) =>
        Assert.That(() =>
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new IceEncoder(bufferWriter);
            encoder.EncodeIceObjectProxy(new IceObjectProxy(InvalidInvoker.Instance, serviceAddress));
        },
        Throws.TypeOf<ArgumentException>());

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

    [Service]
    private partial class MyBaseInterfaceService : IMyBaseInterfaceService, IIceObjectService
    {
    }

    [Service]
    private sealed partial class MyDerivedInterfaceService : MyBaseInterfaceService, IMyDerivedInterfaceService
    {
    }
}
