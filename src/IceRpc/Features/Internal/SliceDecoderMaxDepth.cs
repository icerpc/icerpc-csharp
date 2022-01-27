// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features.Internal
{
    /// <summary>A feature used to set the Slice decoder's max depth property.</summary>
    internal sealed class SliceDecoderMaxDepth
    {
        /// <summary>The maximum depth when decoding a type recursively.</summary>
        internal int Value { get; set; }
    }
}
