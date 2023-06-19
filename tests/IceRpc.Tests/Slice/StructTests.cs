// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Tests.Slice;

[Parallelizable(ParallelScope.All)]
public sealed class StructTests
{
    [Test]
    public void Decode_compact_struct(
    [Values(SliceEncoding.Slice1, SliceEncoding.Slice2)] SliceEncoding encoding)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, encoding);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);

        var decoded = new MyCompactStruct(ref decoder);

        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_struct()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var decoded = new MyStruct(ref decoder);

        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_struct_with_optional_fields(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var bitSequenceWriter = encoder.GetBitSequenceWriter(2);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);

        bitSequenceWriter.Write(k is not null);
        if (k is not null)
        {
            encoder.EncodeInt32(k.Value);
        }

        bitSequenceWriter.Write(l is not null);
        if (l is not null)
        {
            encoder.EncodeInt32(l.Value);
        }
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        var decoded = new MyStructWithOptionalFields(ref decoder);

        // Assert
        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoded.K, Is.EqualTo(k));
        Assert.That(decoded.L, Is.EqualTo(l));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

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
    public void Encode_compact_struct(
        [Values(SliceEncoding.Slice1, SliceEncoding.Slice2)] SliceEncoding encoding)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, encoding);
        var expected = new MyCompactStruct(10, 20);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected.I));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected.J));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_struct()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new MyStruct(10, 20);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected.I));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected.J));
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_struct_with_optional_fields(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new MyStructWithOptionalFields(10, 20, k, l);

        expected.Encode(ref encoder);

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        var bitSequenceReader = decoder.GetBitSequenceReader(2);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(20));

        if (k is not null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeInt32(), Is.EqualTo(k));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }

        if (l is not null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeInt32(), Is.EqualTo(l));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
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

    [Test]
    public void Cs_attribute_on_field()
    {
        // Arrange / Act
        var memberInfos = typeof(MyStructWithFieldAttributes).GetMember("I");
        var attributes = memberInfos[0].GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
        var description = ((System.ComponentModel.DescriptionAttribute)attributes[0]).Description;

        // Assert
        Assert.That(description, Is.EqualTo("An integer"));
    }

    [Test]
    public void Cs_readonly_on_field()
    {
        // Arrange / Act
        var fieldInfo = typeof(MyStructWithFieldAttributes).GetField("J");

        // Assert
        Assert.That(fieldInfo.IsInitOnly);
    }
}
