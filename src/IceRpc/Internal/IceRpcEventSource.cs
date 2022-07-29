// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.Tracing;

namespace IceRpc.Internal;

/// <summary>The event source class of the IceRpc assembly, used for diagnostics logging.</summary>
[EventSource(Name = "IceRpc")]
internal sealed class IceRpcEventSource : EventSource
{
    public static readonly IceRpcEventSource Log = new();

    [Event(1, Level = EventLevel.Informational)]
    public void ValidateConnectionSent() => WriteEvent(1);

    [Event(2, Level = EventLevel.Informational)]
    public void ValidateConnectionReceived() => WriteEvent(2);

    [Event(3, Level = EventLevel.Informational)]
    public void CloseConnectionSent(string reason) => WriteEvent(3, reason);

    [Event(4, Level = EventLevel.Informational)]
    public void CloseConnectionReceived() => WriteEvent(4);

    [Event(5, Level = EventLevel.Informational)]
    public void BatchRequestReceived() => WriteEvent(5);

    [Event(6, Level = EventLevel.Informational)]
    public void GoAwaySent(
        long lastBidirectionalStreamId,
        long lastUnidirectionalStreamId,
        string message) => WriteEvent(6, lastBidirectionalStreamId, lastUnidirectionalStreamId, message);

    [Event(7, Level = EventLevel.Informational)]
    public void GoAwayReceived(
        long lastBidirectionalStreamId,
        long lastUnidirectionalStreamId,
        string message) => WriteEvent(7, lastBidirectionalStreamId, lastUnidirectionalStreamId, message);
}
