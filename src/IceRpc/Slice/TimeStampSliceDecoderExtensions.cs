// Copyright (c) ZeroC, Inc.

using Slice;

namespace IceRpc.Slice;

/// <summary>Provides an extension method for decoding a WellKnownTypes::TimeStamp.</summary>
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
