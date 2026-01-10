// Copyright (c) ZeroC, Inc.

using ZeroC.Slice;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>Provides an extension method for <see cref="SliceEncoder" /> to encode a <see langword="long" /> into a
/// 64-bit opaque data value.</summary>
internal static class OpaqueDataSliceEncoderExtensions
{
    /// <summary>Extension methods for <see cref="SliceEncoder" />.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    extension(ref SliceEncoder encoder)
    {
        /// <summary>Encodes a <see langword="long" /> as a 64-bit opaque data value.</summary>
        /// <param name="value">The value to encode.</param>
        internal void EncodeOpaqueData(long value) => encoder.EncodeInt64(value);
    }
}
