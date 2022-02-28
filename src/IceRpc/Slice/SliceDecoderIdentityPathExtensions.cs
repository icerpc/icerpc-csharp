// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>Provides an extension method for decoding a path encoded as an Ice identity.</summary>
    public static class SliceDecoderIdentityPathExtensions
    {
        /// <summary>Decodes a path encoded as an Ice identity.</summary>
        public static string DecodeIdentityPath(this ref SliceDecoder decoder) => new Identity(ref decoder).ToPath();
    }
}
