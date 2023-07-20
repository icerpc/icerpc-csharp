// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using NUnit.Framework;
using ZeroC.Slice;

namespace IceRpc.Tests.Slice;

[Parallelizable(ParallelScope.All)]
public sealed class StructTests
{
    [Test]
    public void Decode_slice1_compact_struct_with_nullable_proxy(
        [Values("icerpc://localhost/service", null)] string? serviceAddress)
    {
        var expected = new MyCompactStructWithNullableProxy(
            10,
            serviceAddress is null ? null : new PingableProxy { ServiceAddress = new(new Uri(serviceAddress)) });
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeInt32(expected.A);
        encoder.EncodeNullableServiceAddress(expected.I?.ServiceAddress);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        var value = new MyCompactStructWithNullableProxy(ref decoder);

        Assert.That(value, Is.EqualTo(expected));
    }

    [Test]
    public void Decode_slice2_compact_struct_with_nullable_proxy(
        [Values("icerpc://localhost/service", null)] string? serviceAddress)
    {
        var expected = new MyCompactStructWithNullableProxy(
            10,
            serviceAddress is null ? null :
                new PingableProxy { ServiceAddress = new(new Uri(serviceAddress)) });
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var bitSequenceWriter = encoder.GetBitSequenceWriter(1);
        encoder.EncodeInt32(expected.A);
        if (expected.I is null)
        {
            bitSequenceWriter.Write(false);
        }
        else
        {
            bitSequenceWriter.Write(true);
            encoder.EncodeServiceAddress(expected.I.Value.ServiceAddress);
        }
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var value = new MyCompactStructWithNullableProxy(ref decoder);

        Assert.That(value, Is.EqualTo(expected));
    }

    [Test]
    public void Decode_slice1_compact_struct_with_sequence_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithSequenceOfNullableProxies
        {
            I = new PingableProxy?[]
            {
                new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service1")) },
                null,
                new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service2")) },
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeSequence(
            expected.I,
            (ref SliceEncoder encoder, PingableProxy? value) => encoder.EncodeNullableServiceAddress(value?.ServiceAddress));
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        var value = new MyCompactStructWithSequenceOfNullableProxies(ref decoder);

        Assert.That(value.I, Is.EqualTo(expected.I));
    }

    [Test]
    public void Decode_slice2_compact_struct_with_sequence_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithSequenceOfNullableProxies
        {
            I = new PingableProxy?[]
            {
                new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service1")) },
                null,
                new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service2")) },
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeSequenceOfOptionals(
            expected.I,
            (ref SliceEncoder encoder, PingableProxy? value) =>
                encoder.EncodeServiceAddress(value!.Value.ServiceAddress));
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var value = new MyCompactStructWithSequenceOfNullableProxies(ref decoder);

        Assert.That(value.I, Is.EqualTo(expected.I));
    }

    [Test]
    public void Decode_slice1_compact_struct_with_dictionary_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithDictionaryOfNullableProxies
        {
            I = new Dictionary<int, PingableProxy?>
            {
                [1] = new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service1")) },
                [2] = null,
                [3] = new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service2")) },
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeDictionary(
            expected.I,
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            (ref SliceEncoder encoder, PingableProxy? value) => encoder.EncodeNullableServiceAddress(value?.ServiceAddress));
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        var value = new MyCompactStructWithDictionaryOfNullableProxies(ref decoder);

        Assert.That(value.I, Is.EqualTo(expected.I));
    }

    [Test]
    public void Decode_slice2_compact_struct_with_dictionary_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithDictionaryOfNullableProxies
        {
            I = new Dictionary<int, PingableProxy?>
            {
                [1] = new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service1")) },
                [2] = null,
                [3] = new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service2")) },
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeDictionaryWithOptionalValueType(
            expected.I,
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            (ref SliceEncoder encoder, PingableProxy? value) => encoder.EncodeServiceAddress(value!.Value.ServiceAddress));
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var value = new MyCompactStructWithDictionaryOfNullableProxies(ref decoder);

        Assert.That(value.I, Is.EqualTo(expected.I));
    }

    [Test]
    public void Encode_slice1_compact_struct_with_nullable_proxy(
        [Values("icerpc://localhost/service", null)] string? serviceAddress)
    {
        var expected = new MyCompactStructWithNullableProxy(
            10,
            serviceAddress is null ? null : new PingableProxy { ServiceAddress = new(new Uri(serviceAddress)) });
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected.A));
        Assert.That(decoder.DecodeNullableProxy<PingableProxy>(), Is.EqualTo(expected.I));

    }

    [Test]
    public void Encode_slice2_compact_struct_with_nullable_proxy(
        [Values("icerpc://localhost/service", null)] string? serviceAddress)
    {
        var expected = new MyCompactStructWithNullableProxy(
            10,
            serviceAddress is null ? null : new PingableProxy { ServiceAddress = new(new Uri(serviceAddress)) });

        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        var bitSequenceReader = decoder.GetBitSequenceReader(1);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected.A));
        Assert.That(
            bitSequenceReader.Read() ? decoder.DecodeProxy<PingableProxy>() : (PingableProxy?)null,
            Is.EqualTo(expected.I));
    }

    [Test]
    public void Encode_slice1_compact_struct_with_sequence_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithSequenceOfNullableProxies
        {
            I = new PingableProxy?[]
            {
                new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service1")) },
                null,
                new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service2")) },
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);
        Assert.That(
            decoder.DecodeSequence((ref SliceDecoder decoder) => decoder.DecodeNullableProxy<PingableProxy>()),
            Is.EqualTo(expected.I));
    }

    [Test]
    public void Encode_slice2_compact_struct_with_sequence_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithSequenceOfNullableProxies
        {
            I = new PingableProxy?[]
            {
                new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service1")) },
                null,
                new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service2")) },
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(
            decoder.DecodeSequenceOfOptionals<PingableProxy?>(
                (ref SliceDecoder decoder) => decoder.DecodeProxy<PingableProxy>()),
            Is.EqualTo(expected.I));
    }

    [Test]
    public void Encode_slice1_compact_struct_with_dictionary_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithDictionaryOfNullableProxies
        {
            I = new Dictionary<int, PingableProxy?>
            {
                [1] = new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service1")) },
                [2] = null,
                [3] = new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service2")) },
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);
        Assert.That(
            decoder.DecodeDictionary(
                count => new Dictionary<int, PingableProxy?>(count),
                (ref SliceDecoder decoder) => decoder.DecodeInt32(),
                (ref SliceDecoder decoder) => decoder.DecodeNullableProxy<PingableProxy>()),
            Is.EqualTo(expected.I));
    }

    [Test]
    public void Encode_slice2_compact_struct_with_dictionary_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithDictionaryOfNullableProxies
        {
            I = new Dictionary<int, PingableProxy?>
            {
                [1] = new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service1")) },
                [2] = null,
                [3] = new PingableProxy { ServiceAddress = new(new Uri("icerpc://localhost/service2")) },
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(
            decoder.DecodeDictionaryWithOptionalValueType(
                count => new Dictionary<int, PingableProxy?>(count),
                (ref SliceDecoder decoder) => decoder.DecodeInt32(),
                (ref SliceDecoder decoder) => decoder.DecodeProxy<PingableProxy>() as PingableProxy?),
            Is.EqualTo(expected.I));
    }
}
