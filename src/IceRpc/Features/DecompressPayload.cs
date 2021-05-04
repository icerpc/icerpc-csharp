// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features
{
    /// <summary>A feature that specifies whether or not the 2.0 encoded payload of a request or response must be
    /// decompressed.</summary>
    public sealed class DecompressPayload
    {
        /// <summary>A <see cref="DecompressPayload"/> instance that specifies that the 2.0 encoded payload
        /// of a request or response must not be decompressed.</summary>
        public static DecompressPayload No = new DecompressPayload();

        /// <summary>A <see cref="DecompressPayload"/> instance that specifies that the 2.0 encoded payload
        /// of a request or response must be decompressed.</summary>
        public static DecompressPayload Yes = new DecompressPayload();

        private DecompressPayload()
        {
        }
    }
}
