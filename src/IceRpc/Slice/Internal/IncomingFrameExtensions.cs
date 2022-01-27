// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods for incoming frames.</summary>
    internal static class IncomingFrameExtensions
    {
        internal static SliceEncoding GetSlicePayloadEncoding(this IncomingFrame frame) =>
            frame.PayloadEncoding is SliceEncoding encoding ? encoding :
                throw new NotSupportedException($"unsupported encoding '{frame.PayloadEncoding}'");
    }
}
