// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Internal;
using Slice;

namespace IceRpc.Ice;

/// <summary>Provides an extension method for decoding a path encoded as an Ice identity.</summary>
public static class IdentityPathSliceDecoderExtensions
{
    /// <summary>Decodes a path encoded as an Ice identity.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The decoded identity path.</returns>
    public static string DecodeIdentityPath(this ref SliceDecoder decoder) => new Identity(ref decoder).ToPath();
}
