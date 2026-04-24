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
    // adapter-id values containing characters that must be percent-escaped in the URI representation.
    [TestCase("ice:/hello?adapter-id=foo%23bar", null)]                  // '#'
    [TestCase("ice:/hello?adapter-id=foo%25bar", null)]                  // '%'
    [TestCase("ice:/hello?adapter-id=foo%20bar", null)]                  // ' '
    [TestCase("ice:/hello?adapter-id=foo%23bar%25baz%20qux%26quux", null)] // '#', '%', ' ', '&'
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

    // Using transport=quic forces TransportCode.Uri on the ice side (only tcp/ssl have dedicated codes);
    // icerpc always uses TransportCode.Uri.
    [TestCase("ice://hello.zeroc.com/hello?transport=quic", "ice:")]
    [TestCase("icerpc://hello.zeroc.com/hello", "icerpc:")]
    public void Decode_proxy_with_unparseable_server_address_uri_throws_invalid_data(
        ServiceAddress value,
        string schemePrefix)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new IceEncoder(bufferWriter);
        encoder.EncodeIceObjectProxy(new IceObjectProxy(InvalidInvoker.Instance, value));
        int length = bufferWriter.WrittenMemory.Length;

        // Corrupt the first byte of the encoded URI scheme so that new Uri(...) throws UriFormatException.
        // A URI scheme must start with an ALPHA; 0x01 is a control character and makes the URI unparseable.
        int uriStart = buffer.AsSpan(0, length).IndexOf(System.Text.Encoding.UTF8.GetBytes(schemePrefix));
        Assume.That(uriStart, Is.GreaterThanOrEqualTo(0), "scheme not found in encoded buffer");
        buffer[uriStart] = 0x01;

        // Act/Assert
        Assert.That(
            () =>
            {
                var decoder = new IceDecoder(buffer.AsMemory(0, length));
                _ = decoder.DecodeIceObjectProxy();
            },
            Throws.InstanceOf<InvalidDataException>());
    }

    // Using transport=quic forces TransportCode.Uri on the ice side (only tcp/ssl have dedicated codes);
    // icerpc always uses TransportCode.Uri.
    [TestCase("ice://hello.zeroc.com/hello?transport=quic", "ice:", "ftp:")]
    [TestCase("icerpc://hello.zeroc.com/hello", "icerpc:", "gopher:")]
    public void Decode_proxy_with_invalid_server_address_uri_throws_invalid_data(
        ServiceAddress value,
        string schemePrefix,
        string replacementScheme)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new IceEncoder(bufferWriter);
        encoder.EncodeIceObjectProxy(new IceObjectProxy(InvalidInvoker.Instance, value));
        int length = bufferWriter.WrittenMemory.Length;

        // Replace the scheme with one that parses as a URI but is rejected by the ServerAddress constructor,
        // which throws ArgumentException for unsupported protocols. The replacement is chosen with the same
        // byte length so the encapsulation size remains valid.
        Assume.That(replacementScheme.Length, Is.EqualTo(schemePrefix.Length));
        int uriStart = buffer.AsSpan(0, length).IndexOf(System.Text.Encoding.UTF8.GetBytes(schemePrefix));
        Assume.That(uriStart, Is.GreaterThanOrEqualTo(0), "scheme not found in encoded buffer");
        System.Text.Encoding.UTF8.GetBytes(replacementScheme).CopyTo(buffer.AsSpan(uriStart));

        // Act/Assert
        Assert.That(
            () =>
            {
                var decoder = new IceDecoder(buffer.AsMemory(0, length));
                _ = decoder.DecodeIceObjectProxy();
            },
            Throws.InstanceOf<InvalidDataException>());
    }
}
