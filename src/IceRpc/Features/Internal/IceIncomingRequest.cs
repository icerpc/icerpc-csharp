// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features.Internal
{
    /// <summary>A feature that specifies the request ID of an Ice incoming request.</summary>
    internal sealed class IceIncomingRequest
    {
        /// <summary>The request ID.</summary>
        internal int Id { get; }

        internal IceIncomingRequest(int id) => Id = id;
    }
}
