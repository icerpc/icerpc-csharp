// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Extensions;
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
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
        encoder.EncodeServiceAddress(value);
        var decoder = new SliceDecoder(buffer.AsMemory()[0..bufferWriter.WrittenMemory.Length], SliceEncoding.Slice2);

        // Act/Assert
        Assert.That(decoder.DecodeServiceAddress(), Is.EqualTo(value));
    }
}
