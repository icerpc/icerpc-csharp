// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class Slice1NullableTests
{
    [Test]
    public void Using_null_for_non_nullable_proxy_fails_during_decoding()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeNullableServiceAddress(null);

        Assert.That(
            () =>
            {
                var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);
                _ = decoder.DecodeProxy<ServiceProxy>();
            },
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Using_null_for_non_nullable_class_fails_during_decoding()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeNullableClass(null);

        Assert.That(
            () =>
            {
                var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);
                var decoded = decoder.DecodeClass<SliceClass>();
            },
            Throws.TypeOf<InvalidDataException>());
    }
}
