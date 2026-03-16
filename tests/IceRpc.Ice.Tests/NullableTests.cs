// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using IceRpc.Ice.Operations;
using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class NullableTests
{
    [Test]
    public void Using_null_for_non_nullable_proxy_fails_during_decoding()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeNullableServiceAddress(null);

        Assert.That(
            () =>
            {
                var decoder = new IceDecoder(buffer.WrittenMemory);
                _ = decoder.DecodeProxy<Ice.IceObjectProxy>();
            },
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Using_null_for_non_nullable_class_fails_during_decoding()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeNullableClass(null);

        Assert.That(
            () =>
            {
                var decoder = new IceDecoder(buffer.WrittenMemory);
                var decoded = decoder.DecodeClass<IceClass>();
            },
            Throws.TypeOf<InvalidDataException>());
    }
}
