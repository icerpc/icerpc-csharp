// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace ZeroC.Slice.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class ExceptionTests
{
    [Test]
    public void Decode_slice1_derived_exception()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        encoder.StartSlice(typeof(MyDerivedException).GetSliceTypeId()!);
        encoder.EncodeInt32(30);
        encoder.EncodeInt32(40);
        encoder.EndSlice(lastSlice: false);

        encoder.StartSlice(typeof(MyException).GetSliceTypeId()!);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        encoder.EndSlice(lastSlice: true);

        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: IActivator.FromAssembly(typeof(MyException).Assembly));

        var value = decoder.DecodeUserException() as MyDerivedException;

        Assert.That(value, Is.Not.Null);
        Assert.That(value!.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(value.K, Is.EqualTo(30));
        Assert.That(value.L, Is.EqualTo(40));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_slice1_exception([Values(10, null)] int? taggedValue)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.StartSlice(typeof(MyException).GetSliceTypeId()!);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        if (taggedValue is not null)
        {
            // Ensure that a tagged value not declared in the Slice definition is correctly skipped
            encoder.EncodeTagged(
                10,
                TagFormat.F4,
                taggedValue.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }
        encoder.EndSlice(lastSlice: true);
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: IActivator.FromAssembly(typeof(MyException).Assembly));

        var value = decoder.DecodeUserException() as MyException;

        Assert.That(value, Is.Not.Null);
        Assert.That(value!.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_slice1_exception_with_tagged_fields(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.StartSlice(typeof(MyExceptionWithTaggedFields).GetSliceTypeId()!);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        if (k is not null)
        {
            encoder.EncodeTagged(
                1,
                TagFormat.F4,
                k.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }
        if (l is not null)
        {
            encoder.EncodeTagged(
                255,
                TagFormat.F4,
                l.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }
        encoder.EndSlice(lastSlice: true);
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: IActivator.FromAssembly(typeof(MyExceptionWithTaggedFields).Assembly));

        var value = decoder.DecodeUserException() as MyExceptionWithTaggedFields;

        Assert.That(value, Is.Not.Null);
        Assert.That(value!.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(value.K, Is.EqualTo(k));
        Assert.That(value.L, Is.EqualTo(l));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_slice2_exception([Values(10, null)] int? taggedValue)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        if (taggedValue is not null)
        {
            // Ensure that a tagged value not declared in the Slice definition is correctly skipped
            encoder.EncodeTagged(
                10,
                size: 4,
                taggedValue.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var value = new MyException(ref decoder);

        Assert.That(value.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_slice2_exception_with_optional_fields(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
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

        var value = new MyExceptionWithOptionalFields(ref decoder, "");

        Assert.That(value, Is.Not.Null);
        Assert.That(value.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(value.K, Is.EqualTo(k));
        Assert.That(value.L, Is.EqualTo(l));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_slice2_exception_with_tagged_fields(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        if (k is not null)
        {
            encoder.EncodeTagged(
                1,
                size: 4,
                k.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }
        if (l is not null)
        {
            encoder.EncodeTagged(
                255,
                size: 4,
                l.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var value = new MyExceptionWithTaggedFields(ref decoder);

        Assert.That(value, Is.Not.Null);
        Assert.That(value.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(value.K, Is.EqualTo(k));
        Assert.That(value.L, Is.EqualTo(l));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    // Encode

    [Test]
    public void Encode_slice1_derived_exception()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        var expected = new MyDerivedException(10, 20, 30, 40);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: IActivator.FromAssembly(typeof(MyException).Assembly));

        var decoded = decoder.DecodeUserException() as MyDerivedException;
        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.I, Is.EqualTo(expected.I));
        Assert.That(decoded.J, Is.EqualTo(expected.J));
        Assert.That(decoded.K, Is.EqualTo(expected.K));
        Assert.That(decoded.L, Is.EqualTo(expected.L));
    }

    [Test]
    public void Encode_slice1_exception()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        var expected = new MyException(10, 20);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: IActivator.FromAssembly(typeof(MyException).Assembly));
        var value = decoder.DecodeUserException() as MyException;
        Assert.That(value, Is.Not.Null);
        Assert.That(value!.I, Is.EqualTo(expected.I));
        Assert.That(value.J, Is.EqualTo(expected.J));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_slice1_exception_with_tagged_fields(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        var expected = new MyExceptionWithTaggedFields(10, 20, k, l);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: IActivator.FromAssembly(typeof(MyExceptionWithTaggedFields).Assembly));
        var value = decoder.DecodeUserException() as MyExceptionWithTaggedFields;
        Assert.That(value, Is.Not.Null);
        Assert.That(value!.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(value.K, Is.EqualTo(k));
        Assert.That(value.L, Is.EqualTo(l));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_slice2_exception()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var value = new MyException(10, 20, "This is MyException message.");

        value.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.I));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.J));
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_slice2_exception_with_optional_fields(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var value = new MyExceptionWithOptionalFields(10, 20, k, l, "This is my message.");

        value.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        var bitSequenceReader = decoder.GetBitSequenceReader(2);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.I));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.J));
        if (bitSequenceReader.Read())
        {
            Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.K));
        }
        if (bitSequenceReader.Read())
        {
            Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.L));
        }
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_slice2_exception_with_tagged_fields(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var value = new MyExceptionWithTaggedFields(10, 20, k, l, "This is my message.");

        value.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.I));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.J));
        if (k is not null)
        {
            Assert.That(
                decoder.DecodeTagged(1, (ref SliceDecoder decoder) => decoder.DecodeInt32()),
                Is.EqualTo(value.K));
        }
        if (l is not null)
        {
            Assert.That(
                decoder.DecodeTagged(255, (ref SliceDecoder decoder) => decoder.DecodeInt32()),
                Is.EqualTo(value.L));
        }
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}
