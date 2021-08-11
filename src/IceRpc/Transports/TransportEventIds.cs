// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>This enum contains event ID constants used for transport logging.</summary>
    public enum TransportEvent
    {
        /// <summary>The transport failed to accept a connection.</summary>
        AcceptingConnectionFailed = IceRpc.Internal.LoggerExtensions.TransportBaseEventId,
        /// <summary>The transport accepted a new connection.</summary>
        ConnectionAccepted,
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
        /// <seealso cref="Connection.ConnectAsync(System.Threading.CancellationToken)"/>
        ConnectionEstablished,
        /// <summary>The transport received buffer size was adjusted.</summary>
        /// <seealso cref="TcpOptions.ReceiveBufferSize"/>
        ReceiveBufferSizeAdjusted,
        /// <summary>The transport received data.</summary>
        ReceivedData,
        /// <summary>The transport received an invalid datagram message.</summary>
        ReceivedInvalidDatagram,
        /// <summary>The transport send buffer size was adjusted.</summary>
        /// <seealso cref="TcpOptions.SendBufferSize"/>
        SendBufferSizeAdjusted,
        /// <summary>The transport sent data.</summary>
        SentData,
        /// <summary>The transport start accepting connections.</summary>
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
        StopReceivingDatagrams
    }
}
