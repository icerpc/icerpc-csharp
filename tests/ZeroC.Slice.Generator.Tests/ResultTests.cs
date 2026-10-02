// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using ZeroC.Slice.Codec;
using ZeroC.Tests.Common;

namespace ZeroC.Slice.Generator.Tests;

public class ResultTests
{
    [Test]
    public void String_int32_result_encoded_like_compact_variant_enum([Values] bool success)
    {
        // Arrange
        const string successValue = "hello";
        const int failureValue = 123;

        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        StringInt32Result result =
            success ? new StringInt32Result.Success(successValue) : new StringInt32Result.Failure(failureValue);

        encoder.EncodeStringInt32Result(result);

        var decoder = new SliceDecoder(buffer.WrittenMemory);

        // Act
        var holder = new StringInt32ResultHolder(ref decoder);

        // Assert
        Result<string, int> expected = success ? new(successValue) : new(failureValue);
        Assert.That(holder.Value, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(encoder.EncodedByteCount));
    }

    [Test]
    public void String_string_result_wraps_the_values([Values] bool success)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        Result<Success<string>, Failure<string>> result =
            success ? new Success<string>("hello") : new Failure<string>("oops");
        new StringStringResultHolder(result).Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory);

        // Act
        var holder = new StringStringResultHolder(ref decoder);

        // Assert
        Assert.That(holder.Value, Is.EqualTo(result));
        Assert.That(decoder.Consumed, Is.EqualTo(encoder.EncodedByteCount));
    }

    [Test]
    public void Wrap_attribute_wraps_the_values()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        var holder = new WrappedResultHolder(
            new Failure<int>(7),
            [
                new Success<IList<string>>(new List<string> { "a" }),
                new Failure<IList<string?>>(new List<string?> { "b", null }),
            ]);
        holder.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory);

        // Act
        var decoded = new WrappedResultHolder(ref decoder);

        // Assert
        Assert.That(decoded.Value, Is.EqualTo(holder.Value));
        Assert.That(decoded.Values, Has.Count.EqualTo(2));
        Assert.That(decoded.Values[0].Value, Is.InstanceOf<Success<IList<string>>>());
        Assert.That(decoded.Values[1].Value, Is.InstanceOf<Failure<IList<string?>>>());
        Assert.That(
            ((Failure<IList<string?>>)decoded.Values[1].Value!).Value,
            Is.EqualTo(new List<string?> { "b", null }));
        Assert.That(decoder.Consumed, Is.EqualTo(encoder.EncodedByteCount));
    }

    [Test]
    public void Encode_result_that_needs_the_wrap_attribute_fails()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        IList<string?> failure = new List<string?> { "oops", null };
        var holder = new UnwrappedSequencesResultHolder(failure);

        // Act/Assert
        Assert.That(
            () =>
            {
                var encoder = new SliceEncoder(buffer);
                holder.Encode(ref encoder);
            },
            Throws.InstanceOf<NotSupportedException>());
    }

    [Test]
    public void Encode_result_of_integer_arrays_that_needs_the_wrap_attribute_fails()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        IList<int> failure = new int[] { 1, 2 };
        var holder = new UnwrappedIntegerSequencesResultHolder(new(failure));

        // Act/Assert
        Assert.That(
            () =>
            {
                var encoder = new SliceEncoder(buffer);
                holder.Encode(ref encoder);
            },
            Throws.InstanceOf<NotSupportedException>());
    }

    [Test]
    public void Decode_result_of_integer_arrays_that_needs_the_wrap_attribute_fails()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);

        // A List<int> is not an IList<uint>, unlike the int[] the generated code decodes.
        IList<int> failure = new List<int> { 1, 2 };
        new UnwrappedIntegerSequencesResultHolder(new(failure)).Encode(ref encoder);

        // Act/Assert
        Assert.That(
            () =>
            {
                var decoder = new SliceDecoder(buffer.WrittenMemory);
                _ = new UnwrappedIntegerSequencesResultHolder(ref decoder);
            },
            Throws.InstanceOf<NotSupportedException>());
    }

    [TestCase(null)]
    [TestCase(123)]
    public void String_opt_int32_result_encoded_like_compact_variant_enum(int? failureValue)
    {
        // Arrange

        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        var result = new StringOptInt32Result.Failure(failureValue);

        encoder.EncodeStringOptInt32Result(result);

        var decoder = new SliceDecoder(buffer.WrittenMemory);

        // Act
        var holder = new StringOptInt32ResultHolder(ref decoder);

        // Assert
        Result<Success<string>, Failure<int?>> expected = new Failure<int?>(failureValue);
        Assert.That(holder.Value, Is.EqualTo(expected));

        Assert.That(decoder.Consumed, Is.EqualTo(encoder.EncodedByteCount));
    }
}
