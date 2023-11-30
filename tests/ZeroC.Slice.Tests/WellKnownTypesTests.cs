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

    private static IEnumerable<TestCaseData> WellKnownWithOptionalsSource
    {
        get
        {
            yield return new(
                null,
                DateTime.UnixEpoch,
                new Uri("icerpc://localhost/VisitorCenter"),
                null);

            yield return new(
                TimeSpan.FromSeconds(10),
                null,
                null,
                new Guid("cfbe7458-6e8f-45c1-b1d1-404866a9d904"));
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

    [TestCase("2463fecc-45c3-449e-95c7-bf3679dbb220")]
    [TestCase("cfbe7458-6e8f-45c1-b1d1-404866a9d904")]
    public void Decode_uuid(string value)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
        var guid = Guid.Parse(value);
        var span = encoder.GetPlaceholderSpan(16);
        _ = guid.TryWriteBytes(span);

        var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);

        // Act
        Guid decodedGuid = decoder.DecodeUuid();

        Assert.That(decodedGuid, Is.EqualTo(guid));
    }

    [TestCase("2463fecc-45c3-449e-95c7-bf3679dbb220")]
    [TestCase("cfbe7458-6e8f-45c1-b1d1-404866a9d904")]
    public void Encode_uuid(string value)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
        var guid = Guid.Parse(value);

        // Act
        encoder.EncodeUuid(guid);

        var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);

        Span<byte> data = new byte[16];
        decoder.CopyTo(data);
        var decodedGuid = new Guid(data);

        Assert.That(decodedGuid, Is.EqualTo(guid));
    }

    [Test]
    public void Decode_struct_with_custom_fields()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new WellKnown(
            TimeSpan.FromSeconds(10),
            DateTime.UnixEpoch,
            new Uri("icerpc://localhost/VisitorCenter"),
            new Guid("cfbe7458-6e8f-45c1-b1d1-404866a9d904"));

        encoder.EncodeDuration(expected.Duration);
        encoder.EncodeTimeStamp(expected.TimeStamp);
        encoder.EncodeUri(expected.Uri);
        encoder.EncodeUuid(expected.Id);
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var decoded = new WellKnown(ref decoder);

        Assert.That(decoded.Duration, Is.EqualTo(expected.Duration));
        Assert.That(decoded.TimeStamp, Is.EqualTo(expected.TimeStamp));
        Assert.That(decoded.Uri, Is.EqualTo(expected.Uri));
        Assert.That(decoded.Id, Is.EqualTo(expected.Id));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test, TestCaseSource(nameof(WellKnownWithOptionalsSource))]
    public void Decode_struct_with_optional_custom_fields(
        TimeSpan? durationValue,
        DateTime? timeStampValue,
        Uri? uriValue,
        Guid? uuidValue)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new WellKnownWithOptionals(
            durationValue,
            timeStampValue,
            uriValue,
            uuidValue);
        var bitSequenceWriter = encoder.GetBitSequenceWriter(4);

        bitSequenceWriter.Write(durationValue is not null);
        if (durationValue is not null)
        {
            encoder.EncodeDuration(durationValue.Value);
        }

        bitSequenceWriter.Write(timeStampValue is not null);
        if (timeStampValue is not null)
        {
            encoder.EncodeTimeStamp(timeStampValue.Value);
        }

        bitSequenceWriter.Write(uriValue is not null);
        if (uriValue is not null)
        {
            encoder.EncodeUri(uriValue);
        }

        bitSequenceWriter.Write(uuidValue is not null);
        if (uuidValue is not null)
        {
            encoder.EncodeUuid(uuidValue.Value);
        }
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var decoded = new WellKnownWithOptionals(ref decoder);

        Assert.That(decoded.Duration, Is.EqualTo(expected.Duration));
        Assert.That(decoded.TimeStamp, Is.EqualTo(expected.TimeStamp));
        Assert.That(decoded.Uri, Is.EqualTo(expected.Uri));
        Assert.That(decoded.Id, Is.EqualTo(expected.Id));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_struct_with_custom_fields()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new WellKnown(
            TimeSpan.FromSeconds(10),
            DateTime.UnixEpoch,
            new Uri("icerpc://localhost/VisitorCenter"),
            new Guid("cfbe7458-6e8f-45c1-b1d1-404866a9d904"));

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeDuration(), Is.EqualTo(expected.Duration));
        Assert.That(decoder.DecodeTimeStamp(), Is.EqualTo(expected.TimeStamp));
        Assert.That(decoder.DecodeUri(), Is.EqualTo(expected.Uri));
        Assert.That(decoder.DecodeUuid(), Is.EqualTo(expected.Id));
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test, TestCaseSource(nameof(WellKnownWithOptionalsSource))]
    public void Encode_struct_with_optional_custom_fields(
        TimeSpan? durationValue,
        DateTime? timeStampValue,
        Uri? uriValue,
        Guid? uuidValue)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new WellKnownWithOptionals(
            durationValue,
            timeStampValue,
            uriValue,
            uuidValue);

        expected.Encode(ref encoder);

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        var bitSequenceReader = decoder.GetBitSequenceReader(4);
        if (durationValue is not null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeDuration(), Is.EqualTo(expected.Duration));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }

        if (timeStampValue is not null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeTimeStamp(), Is.EqualTo(timeStampValue));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }

        if (uriValue is not null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeUri(), Is.EqualTo(uriValue));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }

        if (uuidValue is not null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeUuid(), Is.EqualTo(uuidValue));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }

        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}
