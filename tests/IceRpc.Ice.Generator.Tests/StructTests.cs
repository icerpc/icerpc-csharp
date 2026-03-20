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
    public void Decode_struct_with_proxy(
        [Values("icerpc://localhost/service", null)] string? serviceAddress)
    {
        var expected = new MyStructWithProxy(
            10,
            serviceAddress is null ? null : new AnotherPingableProxy(InvalidInvoker.Instance, new Uri(serviceAddress)));
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeInt(expected.A);
        encoder.EncodeAnotherPingableProxy(expected.I);
        var decoder = new IceDecoder(buffer.WrittenMemory);

        var value = new MyStructWithProxy(ref decoder);

        Assert.That(value, Is.EqualTo(expected));
    }

    [Test]
    public void Decode_struct_with_sequence_of_proxies()
    {
        var expected = new MyStructWithSequenceOfProxies
        {
            I = new AnotherPingableProxy?[]
            {
                new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service1")),
                null,
                new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service2")),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeSequence(
            expected.I,
            (ref IceEncoder encoder, AnotherPingableProxy? value) => encoder.EncodeAnotherPingableProxy(value));
        var decoder = new IceDecoder(buffer.WrittenMemory);

        var value = new MyStructWithSequenceOfProxies(ref decoder);

        Assert.That(value.I, Is.EqualTo(expected.I));
    }

    [Test]
    public void Decode_struct_with_dictionary_of_proxies()
    {
        var expected = new MyStructWithDictionaryOfProxies
        {
            I = new Dictionary<int, AnotherPingableProxy?>
            {
                [1] = new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service1")),
                [2] = null,
                [3] = new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service2")),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeDictionary(
            expected.I,
            (ref IceEncoder encoder, int value) => encoder.EncodeInt(value),
            (ref IceEncoder encoder, AnotherPingableProxy? value) => encoder.EncodeAnotherPingableProxy(value));
        var decoder = new IceDecoder(buffer.WrittenMemory);

        var value = new MyStructWithDictionaryOfProxies(ref decoder);

        Assert.That(value.I, Is.EqualTo(expected.I));
    }

    [Test]
    public void Encode_struct_with_proxy(
        [Values("icerpc://localhost/service", null)] string? serviceAddress)
    {
        var expected = new MyStructWithProxy(
            10,
            serviceAddress is null ? null : new AnotherPingableProxy(InvalidInvoker.Instance, new Uri(serviceAddress)));
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);

        expected.Encode(ref encoder);

        var decoder = new IceDecoder(buffer.WrittenMemory);
        Assert.That(decoder.DecodeInt(), Is.EqualTo(expected.A));
        Assert.That(decoder.DecodeProxy<AnotherPingableProxy>(), Is.EqualTo(expected.I));

    }

    [Test]
    public void Encode_struct_with_sequence_of_proxies()
    {
        var expected = new MyStructWithSequenceOfProxies
        {
            I = new AnotherPingableProxy?[]
            {
                new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service1")),
                null,
                new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service2")),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);

        expected.Encode(ref encoder);

        var decoder = new IceDecoder(buffer.WrittenMemory);
        Assert.That(
            decoder.DecodeSequence((ref IceDecoder decoder) => decoder.DecodeProxy<AnotherPingableProxy>()),
            Is.EqualTo(expected.I));
    }

    [Test]
    public void Encode_struct_with_dictionary_of_proxies()
    {
        var expected = new MyStructWithDictionaryOfProxies
        {
            I = new Dictionary<int, AnotherPingableProxy?>
            {
                [1] = new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service1")),
                [2] = null,
                [3] = new AnotherPingableProxy(InvalidInvoker.Instance, new Uri("icerpc://localhost/service2")),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);

        expected.Encode(ref encoder);

        var decoder = new IceDecoder(buffer.WrittenMemory);
        Assert.That(
            decoder.DecodeDictionary(
                count => new Dictionary<int, AnotherPingableProxy?>(count),
                (ref IceDecoder decoder) => decoder.DecodeInt(),
                (ref IceDecoder decoder) => decoder.DecodeProxy<AnotherPingableProxy>()),
            Is.EqualTo(expected.I));
    }
}
