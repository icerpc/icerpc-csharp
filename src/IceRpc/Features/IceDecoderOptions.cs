// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc.Features
{
    /// <summary>A feature that overwrites some options of the <see cref="IceDecoder"/> created by the Slice generated
    /// code.</summary>
    public sealed class IceDecoderOptions
    {
        /// <summary>.The maximum depth for a graph of Slice class instances.</summary>
        // TODO: replace by a max depth property that is not class specific.
        // TODO: consider moving to IceRpc.Slice - or do we need to put feature classes in the Features subnamespace?
        public int ClassGraphMaxDepth { get; set; }
    }
}
