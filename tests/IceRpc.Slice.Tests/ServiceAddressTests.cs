// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using ZeroC.Slice.Codec;
using ZeroC.Tests.Common;

namespace IceRpc.Slice.Tests;

/// <summary>Tests the encoding and decoding of service address.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class ServiceAddressTests
{
    [TestCase("icerpc://hello.zeroc.com/hello")]
    public void Encode_decode_service_address(ServiceAddress value)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter);
        encoder.EncodeServiceAddress(value);
        var decoder = new SliceDecoder(buffer.AsMemory()[0..bufferWriter.WrittenMemory.Length]);

        // Act/Assert
        Assert.That(decoder.DecodeServiceAddress(), Is.EqualTo(value));
    }

    [TestCase("icerpc://hello.zeroc.com/hello", "icerpc:")]
    public void Decode_service_address_with_unparsable_uri_throws_invalid_data(
        ServiceAddress value,
        string schemePrefix)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter);
        encoder.EncodeServiceAddress(value);
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
                var decoder = new SliceDecoder(buffer.AsMemory(0, length));
                _ = decoder.DecodeServiceAddress();
            },
            Throws.InstanceOf<InvalidDataException>());
    }

    [TestCase("icerpc://hello.zeroc.com/hello", "icerpc:", "gopher:")]
    public void Decode_service_address_with_invalid_uri_throws_invalid_data(
        ServiceAddress value,
        string schemePrefix,
        string replacementScheme)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter);
        encoder.EncodeServiceAddress(value);
        int length = bufferWriter.WrittenMemory.Length;

        // Replace the scheme with one that parses as a URI but is rejected by the ServiceAddress constructor,
        // which throws ArgumentException for unsupported protocols. The replacement is chosen with the same
        // byte length so the encoded string length prefix remains valid.
        Assert.That(replacementScheme.Length, Is.EqualTo(schemePrefix.Length));
        int uriStart = buffer.AsSpan(0, length).IndexOf(System.Text.Encoding.UTF8.GetBytes(schemePrefix));
        Assert.That(uriStart, Is.GreaterThanOrEqualTo(0), "scheme not found in encoded buffer");
        System.Text.Encoding.UTF8.GetBytes(replacementScheme).CopyTo(buffer.AsSpan(uriStart));

        // Act/Assert
        Assert.That(
            () =>
            {
                var decoder = new SliceDecoder(buffer.AsMemory(0, length));
                _ = decoder.DecodeServiceAddress();
            },
            Throws.InstanceOf<InvalidDataException>());
    }
}
