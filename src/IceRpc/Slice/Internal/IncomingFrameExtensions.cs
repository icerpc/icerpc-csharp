// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods for incoming frames.</summary>
    // TODO: remove
    internal static class IncomingFrameExtensions
    {
        internal static SliceEncoding GetSlicePayloadEncoding(this IncomingRequest request) =>
            request.PayloadEncoding is SliceEncoding encoding ? encoding :
                throw new NotSupportedException($"unsupported encoding '{request.PayloadEncoding}'");
    }
}
