// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceEncoder" /> to encode a <see cref="Uri" /> as a
/// <c>WellKnownTypes::Uri</c>.</summary>
public static class UriSliceEncoderExtensions
{
    /// <summary>Extension methods for <see cref="SliceEncoder" />.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    extension(ref SliceEncoder encoder)
    {
        /// <summary>Encodes a URI.</summary>
        /// <param name="value">The value to encode.</param>
        public void EncodeUri(Uri value) => encoder.EncodeString(value.ToString());
    }
}
