// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>Provides an extension method for encoding a <see langword="long" /> into a 64-bit opaque data value.
/// </summary>
internal static class OpaqueDataSliceEncoderExtensions
{
    /// <summary>Encodes a <see langword="long" /> as a 64-bit opaque data value.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The value to encode.</param>
    internal static void EncodeOpaqueData(this ref SliceEncoder encoder, long value) => encoder.EncodeInt64(value);
}
