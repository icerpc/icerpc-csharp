// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    /// <summary>Extension methods to complete an outgoing frame payload and payload stream.</summary>
    internal static class OutgoingFrameExtensions
    {
        /// <summary>Completes the payloads of the outgoing frame.</summary>
        /// <param name="frame">The incoming frame.</param>
        /// <param name="exception">The completion exception.</param>
        internal static void Complete(this OutgoingFrame frame, Exception? exception = null)
        {
            frame.Payload.Complete(exception);
            frame.PayloadStream?.Complete(exception);
        }
    }
}
