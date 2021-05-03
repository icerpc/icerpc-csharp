// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A feature that specificities whether or not the 2.0 encoded payload of a request or response must be
    /// decompressed.</summary>
    public sealed class DecompressPayloadFeature
    {
        /// <summary>A <see cref="DecompressPayloadFeature"/> instance that specificities that the 2.0 encoded payload
        /// of a request or response must be decompressed.</summary>
        public static DecompressPayloadFeature No = new DecompressPayloadFeature();

        private DecompressPayloadFeature()
        {
        }
    }
}
