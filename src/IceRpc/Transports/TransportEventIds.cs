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

        /// <summary>The transport failed to accept a connection.</summary>
        ListenerAcceptConnectionFailed,

        /// <summary>The listener starts listening for new connections.</summary>
        ListenerListening,

        /// <summary>The listener is shutdown and no longer accepts connections.</summary>
        ListenerShutDown,

        /// <summary>Successfully read data from a transport stream.</summary>
        StreamRead,

        /// <summary>The transport sent data.</summary>
        StreamWrite,

        /// <summary>A network connection was disposed.</summary>
        ConnectionDispose,
    }
}
