// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Extension methods to decode the payload of an incoming frame when this payload is encoded with the Slice
/// encoding.</summary>
public static class IncomingFrameExtensions
{
    /// <summary>Extracts a byte stream PipeReader from an incoming frame.</summary>
    /// <param name="incoming">The incoming frame.</param>
    /// <returns>The byte stream.</returns>
    public static PipeReader DecodeByteStream(this IncomingFrame incoming)
    {
        PipeReader byteStream = incoming.Payload;
        incoming.Payload = InvalidPipeReader.Instance;
        return byteStream;
    }
}
