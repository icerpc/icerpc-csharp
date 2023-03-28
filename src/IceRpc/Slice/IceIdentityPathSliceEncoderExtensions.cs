// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Slice.Internal;

namespace IceRpc;

/// <summary>Provides an extension method for encoding a path as an Ice identity.</summary>
public static class IceIdentityPathSliceEncoderExtensions
{
    /// <summary>Encodes a path as an Ice identity.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The path to encode as an Ice identity.</param>
    public static void EncodeIceIdentityPath(this ref SliceEncoder encoder, string value) =>
        Identity.Parse(value).Encode(ref encoder);
}
