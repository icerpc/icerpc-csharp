// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>This enum contains event ID constants used for transport logging.</summary>
public enum TransportEventId
{
    /// <summary>The listener accepted a new connection.</summary>
    ListenerAccept = IceRpc.Internal.BaseEventId.Transport,

    /// <summary>The listener failed to accept a new connection.</summary>
    ListenerAcceptException,

    /// <summary>The listener is disposed and is no longer accepting connections.</summary>
    ListenerDispose,

    /// <summary>The server transport created a new listener.</summary>
    ServerTransportListen,

    /// <summary>The server transport failed to create a new listener.</summary>
    ServerTransportListenException,
}
