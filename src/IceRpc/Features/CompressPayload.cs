// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features
{
    /// <summary>A feature that specifies whether or not the payload of an ice2 request or response must be
    /// compressed.</summary>
    public sealed class CompressPayload
    {
        /// <summary>A <see cref="CompressPayload"/> instance that specifies that the payload of a request or response
        /// must not be compressed.</summary>
        public static CompressPayload No = new();

        /// <summary>A <see cref="CompressPayload"/> instance that specifies that the payload of a request or response
        /// must be compressed.</summary>
        public static CompressPayload Yes = new();

        private CompressPayload()
        {
        }
    }
}
