// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features.Internal
{
    /// <summary>A feature used to override the class graph max depth used when decoding Slice.</summary>
    internal sealed class ClassGraphMaxDepth
    {
        /// <summary>.The maximum depth for a graph of Slice class instances.</summary>
        // TODO: replace by a max depth property that is not class specific.
        internal int Value { get; set; }
    }
}
