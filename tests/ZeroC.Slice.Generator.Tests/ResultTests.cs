// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
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
