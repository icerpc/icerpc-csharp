// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features
{
    /// <summary>A feature that specifies the request ID of an Ice1 request or response.</summary>
    public sealed class RequestId
    {
        /// <summary>The value of the Ice1 request ID.</summary>
        public int Value { get; init; }
    }
}
