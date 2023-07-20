// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to decode a <c>WellKnownTypes::Duration</c>
/// into a <see cref="TimeSpan"/>.</summary>
public static class DurationSliceDecoderExtensions
{
    /// <summary>Decodes a duration.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The duration decoded as a <see cref="TimeSpan"/>.</returns>
    public static TimeSpan DecodeDuration(this ref SliceDecoder decoder) => new(decoder.DecodeVarInt62());
}
