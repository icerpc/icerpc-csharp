// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using ZeroC.Slice;

namespace IceRpc.Slice.Ice;

/// <summary>Provides an extension for <see cref="SliceEncoder" /> to encode a path as an Ice identity.</summary>
public static class IdentityPathSliceEncoderExtensions
{
    /// <summary>Extension methods for <see cref="SliceEncoder" />.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    extension(ref SliceEncoder encoder)
    {
        /// <summary>Encodes a path as an Ice identity.</summary>
        /// <param name="value">The path to encode as an Ice identity.</param>
        public void EncodeIdentityPath(string value) =>
            Identity.Parse(value).Encode(ref encoder);
    }
}
