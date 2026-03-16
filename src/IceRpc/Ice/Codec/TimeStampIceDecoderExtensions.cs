// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Codec;

/// <summary>Provides an extension method for <see cref="IceDecoder" /> to decode a
/// <c>WellKnownTypes::TimeStamp</c> into a <see cref="DateTime" />.</summary>
public static class TimeStampIceDecoderExtensions
{
    /// <summary>Decodes a time stamp.</summary>
    /// <param name="decoder">The Ice decoder.</param>
    /// <returns>The time stamp decoded as a <see cref="DateTime"/>.</returns>
    public static DateTime DecodeTimeStamp(this ref IceDecoder decoder)
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
