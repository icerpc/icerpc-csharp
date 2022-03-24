// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    /// <summary>Extension methods to complete an outgoing frame pipe reader sources and and pipe writer sink.</summary>
    internal static class OutgoingFrameExtensions
    {
        /// <summary>Completes the pipe reader sources and pipe writer sink of the outgoing frame.</summary>
        /// <param name="frame">The incoming frame.</param>
        /// <param name="exception">The completion exception.</param>
        internal static async ValueTask CompleteAsync(this OutgoingFrame frame, Exception? exception = null)
        {
            await frame.PayloadSink.CompleteAsync(exception).ConfigureAwait(false);
            await frame.PayloadSource.CompleteAsync(exception).ConfigureAwait(false);
            if (frame.PayloadSourceStream != null)
            {
                await frame.PayloadSourceStream.CompleteAsync(exception).ConfigureAwait(false);
            }
        }
    }
}
