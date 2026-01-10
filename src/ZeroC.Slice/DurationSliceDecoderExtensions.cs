// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to decode a <c>WellKnownTypes::Duration</c>
/// into a <see cref="TimeSpan"/>.</summary>
public static class DurationSliceDecoderExtensions
{
    /// <summary>Extension methods for <see cref="SliceDecoder" />.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    extension(ref SliceDecoder decoder)
    {
        /// <summary>Decodes a duration.</summary>
        /// <returns>The duration decoded as a <see cref="TimeSpan"/>.</returns>
        public TimeSpan DecodeDuration() => new(decoder.DecodeVarInt62());
    }
}
