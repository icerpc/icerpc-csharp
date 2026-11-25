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
    [TestCase("ice://hello.zeroc.com/hello?transport=tcp#facet", null)]
    [TestCase("ice://hello.zeroc.com/hello", "ice://hello.zeroc.com/hello?transport=tcp")]
    [TestCase(
        "ice://hello.zeroc.com/hello?alt-server=[::1]?transport=ssl",
        "ice://hello.zeroc.com/hello?transport=tcp&alt-server=[::1]?transport=ssl")]
    // adapter-id values containing characters that must be percent-escaped in the URI representation.
    [TestCase("ice:/hello?adapter-id=foo%23bar", null)] // '#'
    [TestCase("ice:/hello?adapter-id=foo%25bar", null)] // '%'
    [TestCase("ice:/hello?adapter-id=foo%20bar", null)] // ' '
    [TestCase("ice:/hello?adapter-id=foo%23bar%25baz%20qux%26quux", null)] // '#', '%', ' ', '&'

    // adapter-id values that are not percent-escaped
    [TestCase("ice:/hello?adapter-id=foo/bar", null)] // '/'
    [TestCase("ice:/hello?adapter-id=cat:1", null)] // ':'
    [TestCase("ice:/hello?adapter-id=user@host", null)] // '@'
    [TestCase("ice:/hello?adapter-id=a=b,c(d)", null)] // '=', ',', '(', ')'

    // no round-trip here: the URI parser keeps the `%` as is, while the decoder escapes it into `%25`.
    [TestCase("ice:/hello?adapter-id=foo%bar", "ice:/hello?adapter-id=foo%25bar")]
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

    [TestCase(2, 0)]
    [TestCase(1, 1)]
    public void Decode_proxy_with_protocol_version_other_than_1_0_throws_invalid_data(
        byte protocolMajor,
        byte protocolMinor)
    {
        var bufferWriter = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(bufferWriter);
        new Identity("hello", "").Encode(ref encoder);
        encoder.EncodeSize(0); // facet
        encoder.EncodeByte(0); // invocation mode
        encoder.EncodeBool(false); // secure
        encoder.EncodeByte(protocolMajor);
        encoder.EncodeByte(protocolMinor);
        encoder.EncodeByte(1); // encoding major
        encoder.EncodeByte(1); // encoding minor
        encoder.EncodeSize(0); // server address count
        encoder.EncodeString(""); // adapter ID

        // Act/Assert
        Assert.That(
            () =>
            {
                var decoder = new IceDecoder(bufferWriter.WrittenMemory);
                _ = decoder.DecodeIceObjectProxy();
            },
            Throws.InstanceOf<InvalidDataException>());
    }

    // Using transport=quic forces TransportCode.Uri (only tcp/ssl have dedicated codes).
    [TestCase("ice://hello.zeroc.com/hello?transport=quic", "ice:")]
    public void Decode_proxy_with_unparsable_server_address_uri_throws_invalid_data(
        ServiceAddress value,
        string schemePrefix)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new IceEncoder(bufferWriter);
        encoder.EncodeIceObjectProxy(new IceObjectProxy(InvalidInvoker.Instance, value));
        int length = bufferWriter.WrittenMemory.Length;

        // Corrupt the first byte of the encoded URI scheme so that new Uri(...) throws UriFormatException.
        // A URI scheme must start with an ALPHA; 0x01 is a control character and makes the URI unparsable.
        int uriStart = buffer.AsSpan(0, length).IndexOf(System.Text.Encoding.UTF8.GetBytes(schemePrefix));
        Assert.That(uriStart, Is.GreaterThanOrEqualTo(0), "scheme not found in encoded buffer");
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

    // Using transport=quic forces TransportCode.Uri (only tcp/ssl have dedicated codes).
    [TestCase("ice://hello.zeroc.com/hello?transport=quic", "ice:", "ftp:")]
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
        Assert.That(replacementScheme.Length, Is.EqualTo(schemePrefix.Length));
        int uriStart = buffer.AsSpan(0, length).IndexOf(System.Text.Encoding.UTF8.GetBytes(schemePrefix));
        Assert.That(uriStart, Is.GreaterThanOrEqualTo(0), "scheme not found in encoded buffer");
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
