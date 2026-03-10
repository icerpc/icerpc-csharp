// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using NUnit.Framework;
using ZeroC.Slice.Codec;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.CodeGen.Tests;

/// <summary>Test encoding and decoding proxies generated from .ice definitions.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class ProxyTests
{
    /// <summary>Verifies that nullable proxies are correctly encoded with Slice1 encoding.</summary>
    /// <param name="expected">The nullable proxy to test with.</param>
    [TestCase("icerpc://host.zeroc.com/hello")]
    [TestCase(null)]
    public void Decode_slice1_nullable_proxy(ServiceAddress? expected)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeNullableServiceAddress(expected);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        // Act
        AnotherPingableProxy? decoded = decoder.DecodeNullableAnotherPingableProxy();

        // Assert
        Assert.That(decoded?.ServiceAddress, Is.EqualTo(expected));
    }
}
