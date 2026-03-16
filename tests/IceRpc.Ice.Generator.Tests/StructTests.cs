// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using IceRpc.Ice.Operations;
using NUnit.Framework;

using ZeroC.Tests.Common;

namespace IceRpc.Ice.Generator.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class StructTests
{
    [Test]
    public void Decode_slice1_compact_struct_with_nullable_proxy(
        [Values("icerpc://localhost/service", null)] string? serviceAddress)
    {
        var expected = new MyCompactStructWithNullableProxy(
            10,
            serviceAddress is null ? null : new AnotherPingableProxy(InvalidInvoker.Instance, new Uri(serviceAddress)));
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, IceEncoding.Ice1);
        encoder.EncodeInt32(expected.A);
        encoder.EncodeNullableServiceAddress(expected.I?.ServiceAddress);
        var decoder = new IceDecoder(buffer.WrittenMemory, IceEncoding.Ice1);

        var value = new MyCompactStructWithNullableProxy(ref decoder);

        Assert.That(value, Is.EqualTo(expected));
    }

    [Test]
    public void Decode_slice1_compact_struct_with_sequence_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithSequenceOfNullableProxies
        {
            I = new AnotherPingableProxy?[]
            {
                new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service1")),
                null,
                new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service2")),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, IceEncoding.Ice1);
        encoder.EncodeSequence(
            expected.I,
            (ref IceEncoder encoder, AnotherPingableProxy? value) => encoder.EncodeNullableServiceAddress(value?.ServiceAddress));
        var decoder = new IceDecoder(buffer.WrittenMemory, IceEncoding.Ice1);

        var value = new MyCompactStructWithSequenceOfNullableProxies(ref decoder);

        Assert.That(value.I, Is.EqualTo(expected.I));
    }

    [Test]
    public void Decode_slice1_compact_struct_with_dictionary_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithDictionaryOfNullableProxies
        {
            I = new Dictionary<int, AnotherPingableProxy?>
            {
                [1] = new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service1")),
                [2] = null,
                [3] = new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service2")),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, IceEncoding.Ice1);
        encoder.EncodeDictionary(
            expected.I,
            (ref IceEncoder encoder, int value) => encoder.EncodeInt32(value),
            (ref IceEncoder encoder, AnotherPingableProxy? value) => encoder.EncodeNullableServiceAddress(value?.ServiceAddress));
        var decoder = new IceDecoder(buffer.WrittenMemory, IceEncoding.Ice1);

        var value = new MyCompactStructWithDictionaryOfNullableProxies(ref decoder);

        Assert.That(value.I, Is.EqualTo(expected.I));
    }

    [Test]
    public void Encode_slice1_compact_struct_with_nullable_proxy(
        [Values("icerpc://localhost/service", null)] string? serviceAddress)
    {
        var expected = new MyCompactStructWithNullableProxy(
            10,
            serviceAddress is null ? null : new AnotherPingableProxy(InvalidInvoker.Instance, new Uri(serviceAddress)));
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, IceEncoding.Ice1);

        expected.Encode(ref encoder);

        var decoder = new IceDecoder(buffer.WrittenMemory, IceEncoding.Ice1);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected.A));
        Assert.That(decoder.DecodeNullableProxy<AnotherPingableProxy>(), Is.EqualTo(expected.I));

    }

    [Test]
    public void Encode_slice1_compact_struct_with_sequence_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithSequenceOfNullableProxies
        {
            I = new AnotherPingableProxy?[]
            {
                new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service1")),
                null,
                new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service2")),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, IceEncoding.Ice1);

        expected.Encode(ref encoder);

        var decoder = new IceDecoder(buffer.WrittenMemory, IceEncoding.Ice1);
        Assert.That(
            decoder.DecodeSequence((ref IceDecoder decoder) => decoder.DecodeNullableProxy<AnotherPingableProxy>()),
            Is.EqualTo(expected.I));
    }

    [Test]
    public void Encode_slice1_compact_struct_with_dictionary_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithDictionaryOfNullableProxies
        {
            I = new Dictionary<int, AnotherPingableProxy?>
            {
                [1] = new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service1")),
                [2] = null,
                [3] = new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service2")),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, IceEncoding.Ice1);

        expected.Encode(ref encoder);

        var decoder = new IceDecoder(buffer.WrittenMemory, IceEncoding.Ice1);
        Assert.That(
            decoder.DecodeDictionary(
                count => new Dictionary<int, AnotherPingableProxy?>(count),
                (ref IceDecoder decoder) => decoder.DecodeInt32(),
                (ref IceDecoder decoder) => decoder.DecodeNullableProxy<AnotherPingableProxy>()),
            Is.EqualTo(expected.I));
    }
}
