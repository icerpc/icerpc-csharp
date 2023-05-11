// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>Provides an extension method for decoding a <c>WellKnownTypes::Duration</c> into a
/// <see cref="TimeSpan"/>.</summary>
public static class DurationSliceDecoderExtensions
{
    /// <summary>Decodes a duration.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The duration decoded as a <see cref="TimeSpan"/>.</returns>
    public static TimeSpan DecodeDuration(this ref SliceDecoder decoder) => new(decoder.DecodeVarInt62());
}
