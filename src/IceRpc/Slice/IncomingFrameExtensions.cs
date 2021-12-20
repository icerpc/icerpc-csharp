// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Extension methods for incoming frames.</summary>
    public static class IncomingFrameExtensions
    {
        /// <summary>Gets the Slice activator used to locate classes and exceptions during decoding.</summary>
        /// <param name="frame">The incoming frame.</param>
        /// <param name="defaultActivator">The default activator</param>
        public static IActivator GetActivator(this IncomingFrame frame, IActivator defaultActivator) =>
            frame.Features.Get<IActivator>() ?? defaultActivator;

        internal static IceEncoding GetSlicePayloadEncoding(this IncomingFrame frame) =>
            frame.PayloadEncoding is IceEncoding encoding ? encoding :
                throw new NotSupportedException($"unsupported encoding '{frame.PayloadEncoding}'");
    }
}
