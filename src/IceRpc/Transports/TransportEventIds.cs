// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>This enum contains event ID constants used for transport logging.</summary>
    public enum TransportEventIds
    {
        /// <summary>The transport accepted a new connection.</summary>
        ConnectionAccepted = IceRpc.Internal.BaseEventIds.Transport,
        /// <summary>The transport failed to accept a connection.</summary>
        ConnectionAcceptFailed,
        /// <summary>A <see cref="Connection"/> event handler thrown an exception.</summary>
        ConnectionEventHandlerException,
        /// <summary>A <see cref="Connection"/> was closed.</summary>
        ConnectionClosed,
        /// <summary>The <see cref="Connection"/> connect operation failed.</summary>
        /// <seealso cref="Connection.ConnectAsync(System.Threading.CancellationToken)"/>
        ConnectionConnectFailed,
        /// <summary>The connection connect operation succeed.</summary>
        ConnectionEstablished,
        /// <summary>The transport failed to accept a connection.</summary>
        ListenerAcceptConnectionFailed,
        /// <summary>The listener starts listening for new connections.</summary>
        ListenerListening,
        /// <summary>The listener is shutdown and no longer accepts connections.</summary>
        ListenerShutDown,
        /// <summary>The transport received data.</summary>
        ReceivedData,
        /// <summary>The transport received an invalid datagram message.</summary>
        ReceivedInvalidDatagram,
        /// <summary>The transport sent data.</summary>
        SentData,
        /// <summary>The transport starts accepting connections.</summary>
        StartAcceptingConnections,
        /// <summary>The transport starts receiving datagram messages.</summary>
        StartReceivingDatagrams,
        /// <summary>The transport failed to start receiving datagram messages.</summary>
        StartReceivingDatagramsFailed,
        /// <summary>The transport starts sending datagram messages.</summary>
        StartSendingDatagrams,
        /// <summary>The transport failed to start sending datagram messages.</summary>
        StartSendingDatagramsFailed,
        /// <summary>The transport stops accepting connections.</summary>
        StopAcceptingConnections,
        /// <summary>The transport stops receiving datagram messages.</summary>
        StopReceivingDatagrams,

        /// <summary>The TCP server accepted a new connection.</summary>
        TcpNetworkConnectionAccepted,
        /// <summary>The TCP client connection was established.</summary>
        TcpNetworkConnectionEstablished,

        /// <summary>The UDP server starts receiving datagram messages.</summary>
        UdpStartReceivingDatagrams,
        /// <summary>The UDP connection starts sending datagram messages.</summary>
        UdpStartSendingDatagrams,
    }
}
