// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Tests;

/// <summary>Tests the encoding and decoding of service address.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class ServiceAddressTests
{
    [TestCase("icerpc://hello.zeroc.com/hello", null)]
    [TestCase("ice://hello.zeroc.com/hello?transport=tcp#facet", null)]
    [TestCase("ice://hello.zeroc.com/hello", "ice://hello.zeroc.com/hello?transport=tcp")]
    [TestCase(
        "ice://hello.zeroc.com/hello?alt-server=[::1]?transport=ssl",
        "ice://hello.zeroc.com/hello?transport=tcp&alt-server=[::1]?transport=ssl")]
    public void Encode_decode_service_address(ServiceAddress value, ServiceAddress? expectedValue)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new IceEncoder(bufferWriter);
        encoder.EncodeServiceAddress(value);
        var decoder = new IceDecoder(buffer.AsMemory()[0..bufferWriter.WrittenMemory.Length]);

        // Act/Assert
        Assert.That(decoder.DecodeServiceAddress(), Is.EqualTo(expectedValue ?? value));
    }

    [TestCase(null)]
    [TestCase("icerpc://hello.zeroc.com/hello")]
    [TestCase("ice://hello.zeroc.com/hello?transport=tcp#facet")]
    [TestCase("ice://hello.zeroc.com/hello?transport=tcp&alt-server=[::1]?transport=ssl")]
    public void Encode_decode_nullable_service_address(ServiceAddress? value)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new IceEncoder(bufferWriter);
        encoder.EncodeNullableServiceAddress(value);
        var decoder = new IceDecoder(buffer.AsMemory()[0..bufferWriter.WrittenMemory.Length]);

        // Act/Assert
        Assert.That(decoder.DecodeNullableServiceAddress(), Is.EqualTo(value));
    }
}
