// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceEncoder" /> to encode a <see cref="DateTime" /> as a
/// <c>WellKnownTypes::TimeStamp</c>.</summary>
public static class TimeStampSliceEncoderExtensions
{
    /// <summary>Encodes a DateTime as a time stamp.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The value to encode.</param>
    public static void EncodeTimeStamp(this ref SliceEncoder encoder, DateTime value) =>
        encoder.EncodeInt64(value.ToUniversalTime().Ticks);
}
