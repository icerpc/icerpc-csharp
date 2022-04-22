// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>This enum contains event ID constants used for transport logging.</summary>
    public enum TransportEventIds
    {
        /// <summary>The listener failed to accept a connection.</summary>
        ListenerAcceptFailed,

        /// <summary>The listener starts listening for new connections.</summary>
        ListenerCreated,

        /// <summary>The listener is disposed and no longer accepts connections.</summary>
        ListenerDisposed,

        /// <summary>A multiplexed network connection was closed.</summary>
        MultiplexedNetworkConnectionClose,

        /// <summary>Successfully read data from a multiplexed stream.</summary>
        MultiplexedStreamRead,

        /// <summary>Wrote data to a multiplexed stream.</summary>
        MultiplexedStreamWrite,

        /// <summary>Connect on a network connection succeeded.</summary>
        NetworkConnectionConnect = IceRpc.Internal.BaseEventIds.Transport,

        /// <summary>Connect on a network connection failed.</summary>
        NetworkConnectionConnectFailed,

        /// <summary>A network connection was disposed.</summary>
        NetworkConnectionDispose,

        /// <summary>Successfully read data from a simple network connection.</summary>
        SimpleNetworkConnectionRead,

        /// <summary>Wrote data to a simple network connection.</summary>
        SimpleNetworkConnectionWrite,
    }
}
