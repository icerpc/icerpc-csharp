// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Codec;

/// <summary>Provides an extension method for <see cref="IceEncoder" /> to encode a <see cref="TimeSpan"/> as a
/// <c>WellKnownTypes::Duration</c>.</summary>
public static class DurationIceEncoderExtensions
{
    /// <summary>Encodes a time span as a duration.</summary>
    /// <param name="encoder">The Ice encoder.</param>
    /// <param name="value">The value to encode.</param>
    public static void EncodeDuration(this ref IceEncoder encoder, TimeSpan value) =>
        encoder.EncodeVarInt62(value.Ticks);
}
