// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to decode a
/// <c>WellKnownTypes::TimeStamp</c> into a <see cref="DateTime" />.</summary>
public static class TimeStampSliceDecoderExtensions
{
    /// <summary>Decodes a time stamp.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The time stamp decoded as a <see cref="DateTime"/>.</returns>
    public static DateTime DecodeTimeStamp(this ref SliceDecoder decoder)
    {
        long value = decoder.DecodeInt64();
        try
        {
            return new DateTime(value, DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(message: null, exception);
        }
    }
}
