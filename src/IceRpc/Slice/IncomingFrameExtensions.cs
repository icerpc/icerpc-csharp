// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Extension methods for <see cref="IncomingFrame" />.</summary>
public static class IncomingFrameExtensions
{
    /// <summary>Detaches the payload from the incoming frame. The caller takes ownership of the returned payload
    /// pipe reader, and <see cref="IncomingFrame.Payload" /> becomes invalid.</summary>
    /// <param name="incoming">The incoming frame.</param>
    /// <returns>The payload pipe reader.</returns>
    public static PipeReader DetachPayload(this IncomingFrame incoming)
    {
        PipeReader payload = incoming.Payload;
        incoming.Payload = InvalidPipeReader.Instance;
        return payload;
    }
}
