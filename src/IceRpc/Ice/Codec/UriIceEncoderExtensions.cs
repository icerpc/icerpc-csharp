// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Codec;

/// <summary>Provides an extension method for <see cref="IceEncoder" /> to encode a <see cref="Uri" /> as a
/// <c>WellKnownTypes::Uri</c>.</summary>
public static class UriIceEncoderExtensions
{
    /// <summary>Encodes a URI.</summary>
    /// <param name="encoder">The Ice encoder.</param>
    /// <param name="value">The value to encode.</param>
    public static void EncodeUri(this ref IceEncoder encoder, Uri value) => encoder.EncodeString(value.ToString());
}
