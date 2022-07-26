// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>This enum contains event ID constants used for transport logging.</summary>
public enum TransportEventIds
{
    /// <summary>Successfully read data from a duplex connection.</summary>
    DuplexConnectionRead,

    /// <summary>The duplex connection was shut down.</summary>
    DuplexConnectionShutdown,

    /// <summary>The duplex connection failed to shut down.</summary>
    DuplexConnectionShutdownException,

    /// <summary>Wrote data to a duplex connection.</summary>
    DuplexConnectionWrite,

    /// <summary>The listener accepted a new connection.</summary>
    ListenerAccept = IceRpc.Internal.BaseEventIds.Transport,

    /// <summary>The listener failed to accept a new connection.</summary>
    ListenerAcceptException,

    /// <summary>The listener is disposed and is no longer accepting connections.</summary>
    ListenerDispose,

    /// <summary>The multiplexed connection accepted a new stream.</summary>
    MultiplexedConnectionAcceptStream,

    /// <summary>The multiplexed connection failed to accept a new stream.</summary>
    MultiplexedConnectionAcceptStreamException,

    /// <summary>The multiplexed connection created a new stream.</summary>
    MultiplexedConnectionCreateStream,

    /// <summary>The multiplexed connection failed to create a new stream.</summary>
    MultiplexedConnectionCreateStreamException,

    /// <summary>The multiplexed connection was shut down.</summary>
    MultiplexedConnectionShutdown,

    /// <summary>The multiplexed connection failed to shut down.</summary>
    MultiplexedConnectionShutdownException,

    /// <summary>The server transport created a new listener.</summary>
    ServerTransportListen,

    /// <summary>The server transport failed to create a new listener.</summary>
    ServerTransportListenException,
}
