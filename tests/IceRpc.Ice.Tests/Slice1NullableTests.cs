// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using IceRpc.Ice.Operations;
using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class Slice1NullableTests
{
    [Test]
    public void Using_null_for_non_nullable_proxy_fails_during_decoding()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, IceEncoding.Ice1);
        encoder.EncodeNullableServiceAddress(null);

        Assert.That(
            () =>
            {
                var decoder = new IceDecoder(buffer.WrittenMemory, IceEncoding.Ice1);
                _ = decoder.DecodeProxy<Ice.IceObjectProxy>();
            },
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Using_null_for_non_nullable_class_fails_during_decoding()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, IceEncoding.Ice1);
        encoder.EncodeNullableClass(null);

        Assert.That(
            () =>
            {
                var decoder = new IceDecoder(buffer.WrittenMemory, IceEncoding.Ice1);
                var decoded = decoder.DecodeClass<IceClass>();
            },
            Throws.TypeOf<InvalidDataException>());
    }
}
