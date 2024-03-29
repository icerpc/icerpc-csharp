// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceEncoder" /> to encode a <see cref="TimeSpan"/> as a
/// <c>WellKnownTypes::Duration</c>.</summary>
public static class DurationSliceEncoderExtensions
{
    /// <summary>Encodes a time span as a duration.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The value to encode.</param>
    public static void EncodeDuration(this ref SliceEncoder encoder, TimeSpan value) =>
        encoder.EncodeVarInt62(value.Ticks);
}
