// Copyright (c) ZeroC, Inc.

using ZeroC.Slice;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to decode a 64-bit opaque data value into a
/// <see langword="long"/>.</summary>
internal static class OpaqueDataSliceDecoderExtensions
{
    /// <summary>Extension methods for <see cref="SliceDecoder" />.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    extension(ref SliceDecoder decoder)
    {
        /// <summary>Decodes a 64-bit opaque data value.</summary>
        /// <returns>The opaque data value decoded as a <see langword="long"/>.</returns>
        internal long DecodeOpaqueData() => decoder.DecodeInt64();
    }
}
