// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Tests;

/// <summary>Tests the encoding and decoding of proxies.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class ProxyTests
{
    [TestCase(null, null)]
    [TestCase("icerpc://hello.zeroc.com/hello", null)]
    [TestCase("ice://hello.zeroc.com/hello?transport=tcp#facet", null)]
    [TestCase("ice://hello.zeroc.com/hello", "ice://hello.zeroc.com/hello?transport=tcp")]
    [TestCase(
        "ice://hello.zeroc.com/hello?alt-server=[::1]?transport=ssl",
        "ice://hello.zeroc.com/hello?transport=tcp&alt-server=[::1]?transport=ssl")]
    public void Encode_decode_proxy(ServiceAddress? value, ServiceAddress? expectedValue)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new IceEncoder(bufferWriter);
        IceObjectProxy? proxy = value is null ? null : new IceObjectProxy(InvalidInvoker.Instance, value);
        encoder.EncodeIceObjectProxy(proxy);
        var decoder = new IceDecoder(buffer.AsMemory()[0..bufferWriter.WrittenMemory.Length]);

        // Act/Assert
        Assert.That(decoder.DecodeIceObjectProxy()?.ServiceAddress, Is.EqualTo(expectedValue ?? value));
    }
}
