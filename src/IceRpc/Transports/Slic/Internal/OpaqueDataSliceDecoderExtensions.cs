// Copyright (c) ZeroC, Inc.

using ZeroC.Slice;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to  decode a 64-bit opaque data value into a
/// <see langword="long"/>.</summary>
internal static class OpaqueDataSliceDecoderExtensions
{
    /// <summary>Decodes a 64-bit opaque data value.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The opaque data value decoded as a <see langword="long"/>.</returns>
    internal static long DecodeOpaqueData(this ref SliceDecoder decoder) => decoder.DecodeInt64();
}
