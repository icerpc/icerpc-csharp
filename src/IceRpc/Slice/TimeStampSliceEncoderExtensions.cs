// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>Provides an extension method for encoding a DateTime as a WellKnownTypes::TimeStamp.</summary>
public static class TimeStampSliceEncoderExtensions
{
    /// <summary>Encodes a DateTime as a time stamp.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The value to encode.</param>
    public static void EncodeTimeStamp(this ref SliceEncoder encoder, DateTime value) =>
        encoder.EncodeInt64(value.ToUniversalTime().Ticks);
}
