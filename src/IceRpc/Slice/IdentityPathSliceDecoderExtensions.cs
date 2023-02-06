// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice;

/// <summary>Provides an extension method for decoding a path encoded as an Ice identity.</summary>
public static class IdentityPathSliceDecoderExtensions
{
    /// <summary>Decodes a path encoded as an Ice identity.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The decoded Ice identity path.</returns>
    public static string DecodeIdentityPath(this ref SliceDecoder decoder) => new Identity(ref decoder).ToPath();
}
