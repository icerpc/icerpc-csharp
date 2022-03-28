// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    /// <summary>Extension methods to complete an outgoing frame payloads.</summary>
    internal static class OutgoingFrameExtensions
    {
        /// <summary>Completes the payloads of the outgoing frame.</summary>
        /// <param name="frame">The incoming frame.</param>
        /// <param name="exception">The completion exception.</param>
        // TODO: convert to sync method
        internal static async ValueTask CompleteAsync(this OutgoingFrame frame, Exception? exception = null)
        {
            await frame.PayloadSource.CompleteAsync(exception).ConfigureAwait(false);
            if (frame.PayloadSourceStream != null)
            {
                await frame.PayloadSourceStream.CompleteAsync(exception).ConfigureAwait(false);
            }
        }
    }
}
