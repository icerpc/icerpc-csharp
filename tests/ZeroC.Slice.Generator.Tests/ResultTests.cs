// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
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
        if (success)
        {
            holder.Value.MatchSuccess(
                success => Assert.That(success.Value, Is.EqualTo(successValue)),
                () => Assert.Fail("Expected success"));
        }
        else
        {
            holder.Value.MatchFailure(
                failure => Assert.That(failure.Value, Is.EqualTo(failureValue)),
                () => Assert.Fail("Expected failure"));
        }
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
        holder.Value.MatchFailure(
            failure => Assert.That(failure.Value, Is.EqualTo(failureValue)),
            () => Assert.Fail("Expected failure"));

        Assert.That(decoder.Consumed, Is.EqualTo(encoder.EncodedByteCount));
    }

    [Test]
    public void Decode_result_with_custom_sequence_success_type()
    {
        // Arrange
        int[] expected = [1, 2, 3];
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        new CustomSequenceResultHolder(new Result<IList<int>, string>.Success(expected)).Encode(ref encoder);
        var decoder = new SliceDecoder(buffer.WrittenMemory);

        // Act
        var holder = new CustomSequenceResultHolder(ref decoder);

        // Assert
        holder.Value.MatchSuccess(
            success => Assert.That(success.Value, Is.EqualTo(expected).And.InstanceOf<List<int>>()),
            () => Assert.Fail("Expected success"));
        Assert.That(decoder.Consumed, Is.EqualTo(encoder.EncodedByteCount));
    }
}
