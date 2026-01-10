// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using ZeroC.Slice;

namespace IceRpc.Slice.Ice;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to decode a path encoded as an Ice
/// identity.</summary>
public static class IdentityPathSliceDecoderExtensions
{
    /// <summary>Extension methods for <see cref="SliceDecoder" />.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    extension(ref SliceDecoder decoder)
    {
        /// <summary>Decodes a path encoded as an Ice identity.</summary>
        /// <returns>The decoded identity path.</returns>
        public string DecodeIdentityPath() => new Identity(ref decoder).ToPath();
    }
}
