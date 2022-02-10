// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>Provides an extension method for encoding a path as an Ice identity.</summary>
    public static class SliceEncoderIdentityPathExtensions
    {
        /// <summary>Encodes a path as an Ice identity.</summary>
        public static void EncodeIdentityPath(this ref SliceEncoder encoder, string value) =>
            Identity.Parse(value).Encode(ref encoder);
    }
}
