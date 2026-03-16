// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Codec;

/// <summary>Provides an extension method for <see cref="IceEncoder" /> to encode a <see cref="DateTime" /> as a
/// <c>WellKnownTypes::TimeStamp</c>.</summary>
public static class TimeStampIceEncoderExtensions
{
    /// <summary>Encodes a DateTime as a time stamp.</summary>
    /// <param name="encoder">The Ice encoder.</param>
    /// <param name="value">The value to encode.</param>
    public static void EncodeTimeStamp(this ref IceEncoder encoder, DateTime value) =>
        encoder.EncodeInt64(value.ToUniversalTime().Ticks);
}
