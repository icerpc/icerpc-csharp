// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A feature that specificities whether or not the 2.0 encoded Payload of a request or response must be
    /// compressed.</summary>
    public sealed class CompressPayloadFeature
    {
        /// <summary>A <see cref="CompressPayloadFeature"/> instance that specificities that the 2.0 encoded Payload of
        /// a request or response must be compressed.</summary>
        public static CompressPayloadFeature Yes = new CompressPayloadFeature();

        private CompressPayloadFeature()
        {
        }
    }
}
