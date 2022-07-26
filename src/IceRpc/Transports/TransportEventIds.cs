// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>This enum contains event ID constants used for transport logging.</summary>
public enum TransportEventIds
{
    /// <summary>The listener failed to accept a connection.</summary>
    ListenerAcceptFailed = IceRpc.Internal.BaseEventIds.Transport,

    /// <summary>The listener starts listening for new connections.</summary>
    ListenerCreated,

    /// <summary>The listener is disposed and no longer accepts connections.</summary>
    ListenerDisposed,

    /// <summary>A multiplexed connection was shutdown.</summary>
    MultiplexedConnectionShutdown,

    /// <summary>Successfully read data from a duplex connection.</summary>
    DuplexConnectionRead,

    /// <summary>Single stream transport connection shutdown.</summary>
    DuplexConnectionShutdown,

    /// <summary>Wrote data to a duplex connection.</summary>
    DuplexConnectionWrite,
}
