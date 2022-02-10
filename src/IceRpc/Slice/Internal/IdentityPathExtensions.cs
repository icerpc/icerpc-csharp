// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods to encode and decode a path into/from an <see cref="Identity"/>.</summary>
    public static class IdentityPathExtensions // TODO: see https://github.com/zeroc-ice/icerpc-csharp/issues/786
    {
        /// <summary>Decodes a path from an identity representation.</summary>
        public static string DecodeIdentityPath(this ref SliceDecoder decoder) => new Identity(ref decoder).ToPath();

        /// <summary>Encodes a path as an identity.</summary>
        public static void EncodeIdentityPath(this ref SliceEncoder encoder, string value) =>
            Identity.Parse(value).Encode(ref encoder);
    }
}
