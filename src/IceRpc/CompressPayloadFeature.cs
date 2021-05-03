// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A feature that specificities that the Payload of Ice2 request or response must be compressed.
    /// </summary>
    public sealed class CompressPayloadFeature
    {
        public static CompressPayloadFeature Yes = new CompressPayloadFeature();

        private CompressPayloadFeature()
        {
        }
    }
}
