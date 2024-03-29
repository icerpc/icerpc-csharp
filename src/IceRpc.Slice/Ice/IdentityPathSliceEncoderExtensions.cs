// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using ZeroC.Slice;

namespace IceRpc.Slice.Ice;

/// <summary>Provides an extension for <see cref="SliceEncoder" /> to encode a path as an Ice identity.</summary>
public static class IdentityPathSliceEncoderExtensions
{
    /// <summary>Encodes a path as an Ice identity.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The path to encode as an Ice identity.</param>
    public static void EncodeIdentityPath(this ref SliceEncoder encoder, string value) =>
        Identity.Parse(value).Encode(ref encoder);
}
