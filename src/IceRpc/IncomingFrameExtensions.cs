// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>Provides an extension method for <see cref="IncomingFrame" /> to detach its payload.</summary>
public static class IncomingFrameExtensions
{
    /// <summary>Extension methods for <see cref="IncomingFrame" />.</summary>
    /// <param name="incoming">The incoming frame.</param>
    extension(IncomingFrame incoming)
    {
        /// <summary>Detaches the payload from the incoming frame. The caller takes ownership of the returned payload
        /// pipe reader, and <see cref="IncomingFrame.Payload" /> becomes invalid.</summary>
        /// <returns>The payload pipe reader.</returns>
        public PipeReader DetachPayload()
        {
            PipeReader payload = incoming.Payload;
            incoming.Payload = InvalidPipeReader.Instance;
            return payload;
        }
    }
}
