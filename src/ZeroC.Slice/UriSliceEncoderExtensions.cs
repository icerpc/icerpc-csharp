// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceEncoder" /> to encode a <see cref="Uri" /> as a
/// <c>WellKnownTypes::Uri</c>.</summary>
public static class UriSliceEncoderExtensions
{
    /// <summary>Encodes a URI.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The value to encode.</param>
    public static void EncodeUri(this ref SliceEncoder encoder, Uri value) => encoder.EncodeString(value.ToString());
}
