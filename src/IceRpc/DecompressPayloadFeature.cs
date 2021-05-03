// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A feature that specificities that the Payload of Ice2 request or response must be decompressed.
    /// </summary>
    public sealed class DecompressPayloadFeature
    {
        public static DecompressPayloadFeature No = new DecompressPayloadFeature();
        public static DecompressPayloadFeature Yes = new DecompressPayloadFeature();

        private DecompressPayloadFeature()
        {
        }
    }
}
