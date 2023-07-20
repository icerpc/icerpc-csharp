// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace ZeroC.Slice.Tests;

/// <summary>Tests the encoding and decoding of the well-known types.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class WellKnownTypesTests
{
    private static IEnumerable<TestCaseData> DurationSource
    {
        get
        {
            yield return new(TimeSpan.FromSeconds(10));
            yield return new(TimeSpan.FromSeconds(-10));
            yield return new(new TimeSpan(SliceEncoder.VarInt62MaxValue));
            yield return new(new TimeSpan(SliceEncoder.VarInt62MinValue));
        }
    }

    private static IEnumerable<TestCaseData> TimeStampSource
    {
        get
        {
            yield return new(DateTime.Now);
            yield return new(DateTime.MaxValue);
            yield return new(DateTime.MinValue);
        }
    }

    [Test, TestCaseSource(nameof(DurationSource))]
    public void Decode_duration(TimeSpan duration)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
        encoder.EncodeVarInt62(duration.Ticks);

        var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);

        // Act
        var decodedDuration = decoder.DecodeDuration();

        Assert.That(decodedDuration, Is.EqualTo(duration));
    }

    [Test, TestCaseSource(nameof(DurationSource))]
    public void Encode_duration(TimeSpan duration)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        // Act
        encoder.EncodeDuration(duration);

        var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
        var decodedDuration = new TimeSpan(decoder.DecodeVarInt62());
        Assert.That(decodedDuration, Is.EqualTo(duration));
    }

    [Test, TestCaseSource(nameof(TimeStampSource))]
    public void Decode_timeStamp(DateTime timeStamp)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
        encoder.EncodeInt64(timeStamp.ToUniversalTime().Ticks);

        var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);

        // Act
        var decodedTimeStamp = decoder.DecodeTimeStamp();

        Assert.That(decodedTimeStamp, Is.EqualTo(timeStamp.ToUniversalTime()));
    }

    [Test, TestCaseSource(nameof(TimeStampSource))]
    public void Encode_timeStamp(DateTime timeStamp)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        // Act
        encoder.EncodeTimeStamp(timeStamp);

        var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
        var decodedTimeStamp = new DateTime(decoder.DecodeInt64(), DateTimeKind.Utc);

        Assert.That(decodedTimeStamp, Is.EqualTo(timeStamp.ToUniversalTime()));
    }

    [TestCase("https://zeroc.com/foo?p1=v1")]
    [TestCase("ice://host/foo#bar")]
    [TestCase("/relative")]
    [TestCase("../relative")]
    public void Decode_uri(Uri uri)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
        encoder.EncodeString(uri.ToString());

        var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);

        // Act
        Uri decodedUri = decoder.DecodeUri();

        Assert.That(decodedUri, Is.EqualTo(uri));
    }

    [TestCase("https://zeroc.com/foo?p1=v1")]
    [TestCase("ice://host/foo#bar")]
    [TestCase("/relative")]
    [TestCase("../relative")]
    public void Encode_uri(Uri uri)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        // Act
        encoder.EncodeUri(uri);

        var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
        var decodedUri = new Uri(decoder.DecodeString(), UriKind.RelativeOrAbsolute);

        Assert.That(decodedUri, Is.EqualTo(uri));
    }
}
