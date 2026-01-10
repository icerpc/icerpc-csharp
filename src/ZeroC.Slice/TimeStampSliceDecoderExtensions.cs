// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to decode a
/// <c>WellKnownTypes::TimeStamp</c> into a <see cref="DateTime" />.</summary>
public static class TimeStampSliceDecoderExtensions
{
    /// <summary>Extension methods for <see cref="SliceDecoder" />.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    extension(ref SliceDecoder decoder)
    {
        /// <summary>Decodes a time stamp.</summary>
        /// <returns>The time stamp decoded as a <see cref="DateTime"/>.</returns>
        public DateTime DecodeTimeStamp()
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
}
