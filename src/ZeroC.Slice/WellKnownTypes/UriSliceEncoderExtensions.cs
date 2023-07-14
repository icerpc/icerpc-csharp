// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.WellKnownTypes;

/// <summary>Provides an extension method for encoding a URI as a WellKnownTypes::Uri.</summary>
public static class UriSliceEncoderExtensions
{
    /// <summary>Encodes a URI.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The value to encode.</param>
    public static void EncodeUri(this ref SliceEncoder encoder, Uri value) => encoder.EncodeString(value.ToString());
}
