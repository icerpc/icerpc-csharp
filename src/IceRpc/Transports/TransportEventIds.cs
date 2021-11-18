// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>This enum contains event ID constants used for transport logging.</summary>
    public enum TransportEventIds
    {
        /// <summary>Connect on a network connection succeeded.</summary>
        Connect = IceRpc.Internal.BaseEventIds.Transport,

        /// <summary>Connect on a network connection failed.</summary>
        ConnectFailed,

        /// <summary>The listener failed to accept a connection.</summary>
        ListenerAcceptFailed,

        /// <summary>The listener starts listening for new connections.</summary>
        ListenerCreated,

        /// <summary>The listener is disposed and no longer accepts connections.</summary>
        ListenerDisposed,

         /// <summary>Successfully read data from a multiplexed stream.</summary>
        MultiplexedStreamRead,

        /// <summary>Wrote data to a multiplexed stream.</summary>
        MultiplexedStreamWrite,

        /// <summary>Successfully read data from a simple stream.</summary>
        SimpleNetworkConnectionRead,

        /// <summary>Wrote data to a simple stream.</summary>
        SimpleNetworkConnectionWrite,

        /// <summary>A network connection was disposed.</summary>
        ConnectionDispose,
    }
}
