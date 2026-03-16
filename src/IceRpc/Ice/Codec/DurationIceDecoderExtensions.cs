// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Codec;

/// <summary>Provides an extension method for <see cref="IceDecoder" /> to decode a <c>WellKnownTypes::Duration</c>
/// into a <see cref="TimeSpan"/>.</summary>
public static class DurationIceDecoderExtensions
{
    /// <summary>Decodes a duration.</summary>
    /// <param name="decoder">The Ice decoder.</param>
    /// <returns>The duration decoded as a <see cref="TimeSpan"/>.</returns>
    public static TimeSpan DecodeDuration(this ref IceDecoder decoder) => new(decoder.DecodeVarInt62());
}
