// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    /// <summary>Extension methods to complete an outgoing frame payload and payload stream.</summary>
    internal static class OutgoingFrameExtensions
    {
        /// <summary>Completes the payloads of the outgoing frame.</summary>
        /// <param name="frame">The incoming frame.</param>
        /// <param name="exception">The completion exception.</param>
        // TODO: convert to sync method
        internal static async ValueTask CompleteAsync(this OutgoingFrame frame, Exception? exception = null)
        {
            await frame.Payload.CompleteAsync(exception).ConfigureAwait(false);
            if (frame.PayloadStream != null)
            {
                await frame.PayloadStream.CompleteAsync(exception).ConfigureAwait(false);
            }
        }
    }
}
