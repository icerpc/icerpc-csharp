// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Timeout(5000)]
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
    public void Decode_trait()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeString(MyStruct.SliceTypeId);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice2,
            activator: SliceDecoder.GetActivator(typeof(MyStruct).Assembly));

        MyStruct decoded = decoder.DecodeTrait<MyStruct>();

        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_struct_with_optional_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var bitSequenceWriter = encoder.GetBitSequenceWriter(2);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);

        bitSequenceWriter.Write(k != null);
        if (k != null)
        {
            encoder.EncodeInt32(k.Value);
        }

        bitSequenceWriter.Write(l != null);
        if (l != null)
        {
            encoder.EncodeInt32(l.Value);
        }
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        var decoded = new MyStructWithOptionalMembers(ref decoder);

        // Assert
        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoded.K, Is.EqualTo(k));
        Assert.That(decoded.L, Is.EqualTo(l));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_struct_with_tagged_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l,
        [Values(30ul, null)] ulong? m)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        if (k != null)
        {
            encoder.EncodeTagged(
                1,
                size: 4,
                k.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }

        if (l != null)
        {
            encoder.EncodeTagged(
                255,
                size: 1,
                l.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeVarInt32(value));
        }

        if (m != null)
        {
            encoder.EncodeTagged(
                256,
                size: 1,
                m.Value,
                (ref SliceEncoder encoder, ulong value) => encoder.EncodeVarUInt62(value));
        }
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var decoded = new MyStructWithTaggedMembers(ref decoder);
        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoded.K, Is.EqualTo(k));
        Assert.That(decoded.L, Is.EqualTo(l));
        Assert.That(decoded.M, Is.EqualTo(m));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_slice1_struct_with_nullable_proxy(
        [Values("icerpc://localhost/service", null)] string? proxy)
    {
        var expected = new MyCompactStructWithNullabeProxy(
            10,
            proxy == null ? null : ServicePrx.Parse(proxy));
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeInt32(expected.A);
        encoder.EncodeNullableProxy(expected.I?.Proxy);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        var value = new MyCompactStructWithNullabeProxy(ref decoder);

        Assert.That(value, Is.EqualTo(expected));

    }

    [Test]
    public void Decode_slice2_struct_with_nullable_proxy(
        [Values("icerpc://localhost/service", null)]string? proxy)
    {
        var expected = new MySlice2OnlyCompactStructWithNullabeProxy(
            10,
            proxy == null ? null : ServicePrx.Parse(proxy));
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var bitSequenceWriter = encoder.GetBitSequenceWriter(1);
        encoder.EncodeInt32(expected.A);
        if (expected.I == null)
        {
            bitSequenceWriter.Write(false);
        }
        else
        {
            bitSequenceWriter.Write(true);
            encoder.EncodeNullableProxy(ref bitSequenceWriter, expected.I.Value.Proxy);
        }
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var value = new MySlice2OnlyCompactStructWithNullabeProxy(ref decoder);

        Assert.That(value, Is.EqualTo(expected));
    }

    [Test]
    public void Decode_slice1_struct_with_sequence_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithSequenceOfNullableProxies
        {
            I = new ServicePrx?[]
            {
                ServicePrx.Parse("icerpc://localhost/service1"),
                null,
                ServicePrx.Parse("icerpc://localhost/service2"),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeSequence(
            expected.I,
            (ref SliceEncoder encoder, ServicePrx? value) => encoder.EncodeNullableProxy(value?.Proxy));
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        var value = new MyCompactStructWithSequenceOfNullableProxies(ref decoder);

        Assert.That(value.I, Is.EqualTo(expected.I));
    }

    [Test]
    public void Decode_slice2_struct_with_sequence_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithSequenceOfNullableProxies
        {
            I = new ServicePrx?[]
            {
                ServicePrx.Parse("icerpc://localhost/service1"),
                null,
                ServicePrx.Parse("icerpc://localhost/service2"),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeSequenceWithBitSequence(
            expected.I,
            (ref SliceEncoder encoder, ServicePrx? value) => encoder.EncodeProxy(value!.Value.Proxy));
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var value = new MyCompactStructWithSequenceOfNullableProxies(ref decoder);

        Assert.That(value.I, Is.EqualTo(expected.I));
    }

    [Test]
    public void Decode_slice1_struct_with_dictionary_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithDictionaryOfNullableProxies
        {
            I = new Dictionary<int, ServicePrx?>
            {
                [1] = ServicePrx.Parse("icerpc://localhost/service1"),
                [2] = null,
                [3] = ServicePrx.Parse("icerpc://localhost/service2"),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeDictionary(
            expected.I,
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            (ref SliceEncoder encoder, ServicePrx? value) => encoder.EncodeNullableProxy(value?.Proxy));
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        var value = new MyCompactStructWithDictionaryOfNullableProxies(ref decoder);

        Assert.That(value.I, Is.EqualTo(expected.I));
    }

    [Test]
    public void Decode_slice2_struct_with_dictionary_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithDictionaryOfNullableProxies
        {
            I = new Dictionary<int, ServicePrx?>
            {
                [1] = ServicePrx.Parse("icerpc://localhost/service1"),
                [2] = null,
                [3] = ServicePrx.Parse("icerpc://localhost/service2"),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeDictionaryWithBitSequence(
            expected.I,
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            (ref SliceEncoder encoder, ServicePrx? value) => encoder.EncodeProxy(value!.Value.Proxy));
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
    public void Encode_trait()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new MyStruct(10, 20);

        expected.EncodeTrait(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyStruct.SliceTypeId));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(20));
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_struct_with_optional_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new MyStructWithOptionalMembers(10, 20, k, l);

        expected.Encode(ref encoder);

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        var bitSequenceReader = decoder.GetBitSequenceReader(2);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(20));

        if (k != null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeInt32(), Is.EqualTo(k));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }

        if (l != null)
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
    public void Encode_struct_with_tagged_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l,
        [Values(30ul, null)] ulong? m)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new MyStructWithTaggedMembers(10, 20, k, l, m);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(20));
        if (k != null)
        {
            Assert.That(
                decoder.DecodeTagged(1, (ref SliceDecoder decoder) => decoder.DecodeInt32(), useTagEndMarker: true),
                Is.EqualTo(k));
        }

        if (l != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    255,
                    (ref SliceDecoder decoder) => decoder.DecodeVarInt32(),
                    useTagEndMarker: true),
                Is.EqualTo(l));
        }

        if (m != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    256,
                    (ref SliceDecoder decoder) => decoder.DecodeVarUInt62(),
                    useTagEndMarker: true),
                Is.EqualTo(m));
        }
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_slice1_struct_with_nullable_proxy(
        [Values("icerpc://localhost/service", null)] string? proxy)
    {
        var expected = new MyCompactStructWithNullabeProxy(
            10,
            proxy == null ? null : ServicePrx.Parse(proxy));
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected.A));
        Assert.That(decoder.DecodeNullablePrx<ServicePrx>(), Is.EqualTo(expected.I));

    }

    [Test]
    public void Encode_slice2_struct_with_nullable_proxy(
        [Values("icerpc://localhost/service", null)] string? proxy)
    {
        var expected = new MySlice2OnlyCompactStructWithNullabeProxy(
            10,
            proxy == null ? null : ServicePrx.Parse(proxy));
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        var bitSequenceReader = decoder.GetBitSequenceReader(1);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected.A));
        Assert.That(decoder.DecodeNullablePrx<ServicePrx>(ref bitSequenceReader), Is.EqualTo(expected.I));
    }

    [Test]
    public void Encode_slice1_struct_with_sequence_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithSequenceOfNullableProxies
        {
            I = new ServicePrx?[]
            {
                ServicePrx.Parse("icerpc://localhost/service1"),
                null,
                ServicePrx.Parse("icerpc://localhost/service2"),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);
        Assert.That(
            decoder.DecodeSequence((ref SliceDecoder decoder) => decoder.DecodeNullablePrx<ServicePrx>()),
            Is.EqualTo(expected.I));
    }

    [Test]
    public void Encode_slice2_struct_with_sequence_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithSequenceOfNullableProxies
        {
            I = new ServicePrx?[]
            {
                ServicePrx.Parse("icerpc://localhost/service1"),
                null,
                ServicePrx.Parse("icerpc://localhost/service2"),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(
            decoder.DecodeSequenceWithBitSequence<ServicePrx?>(
                (ref SliceDecoder decoder) => new ServicePrx(decoder.DecodeProxy())),
            Is.EqualTo(expected.I));
    }

    [Test]
    public void Encode_slice1_struct_with_dictionary_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithDictionaryOfNullableProxies
        {
            I = new Dictionary<int, ServicePrx?>
            {
                [1] = ServicePrx.Parse("icerpc://localhost/service1"),
                [2] = null,
                [3] = ServicePrx.Parse("icerpc://localhost/service2"),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);
        Assert.That(
            decoder.DecodeDictionary(
                count => new Dictionary<int, ServicePrx?>(count),
                (ref SliceDecoder decoder) => decoder.DecodeInt32(),
                (ref SliceDecoder decoder) => decoder.DecodeNullablePrx<ServicePrx>()),
            Is.EqualTo(expected.I));
    }

    [Test]
    public void Encode_slice2_struct_with_dictionary_of_nullable_proxies()
    {
        var expected = new MyCompactStructWithDictionaryOfNullableProxies
        {
            I = new Dictionary<int, ServicePrx?>
            {
                [1] = ServicePrx.Parse("icerpc://localhost/service1"),
                [2] = null,
                [3] = ServicePrx.Parse("icerpc://localhost/service2"),
            }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(
            decoder.DecodeDictionaryWithBitSequence(
                count => new Dictionary<int, ServicePrx?>(count),
                (ref SliceDecoder decoder) => decoder.DecodeInt32(),
                (ref SliceDecoder decoder) => new ServicePrx(decoder.DecodeProxy()) as ServicePrx?),
            Is.EqualTo(expected.I));
    }
}
