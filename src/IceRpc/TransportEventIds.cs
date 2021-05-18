// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for transport logging.</summary>
    public static class TransportEventIds
    {
        /// <summary>The transport failed to accept a connection.</summary>
        public static readonly EventId AcceptingConnectionFailed =
            GetEventId(TransportEvent.AcceptingConnectionFailed);

        /// <summary>The transport accepted a new connection.</summary>
        public static readonly EventId ConnectionAccepted = GetEventId(TransportEvent.ConnectionAccepted);

        /// <summary>The transport failed to accept a connection.</summary>
        public static readonly EventId ConnectionAcceptFailed = GetEventId(TransportEvent.ConnectionAcceptFailed);
        
        /// <summary>A <see cref="Connection"/> event handler thrown an exception.</summary>
        public static readonly EventId ConnectionEventHandlerException =
            GetEventId(TransportEvent.ConnectionEventHandlerException);

        /// <summary>A <see cref="Connection"/> was closed.</summary>
        public static readonly EventId ConnectionClosed = GetEventId(TransportEvent.ConnectionClosed);

        /// <summary>The <see cref="Connection"/> connect operation failed.</summary>
        /// <seealso cref="Connection.ConnectAsync(System.Threading.CancellationToken)"/>
        public static readonly EventId ConnectionConnectFailed = GetEventId(TransportEvent.ConnectionConnectFailed);

        /// <summary>The connection connect operation succeed.</summary>
        /// <seealso cref="Connection.ConnectAsync(System.Threading.CancellationToken)"/>
        public static readonly EventId ConnectionConnected = GetEventId(TransportEvent.ConnectionEstablished);

        /// <summary>The transport received buffer size was adjusted.</summary>
        /// <seealso cref="TcpOptions.ReceiveBufferSize"/>
        /// <seealso cref="UdpOptions.ReceiveBufferSize"/>
        public static readonly EventId ReceiveBufferSizeAdjusted =
            GetEventId(TransportEvent.ReceiveBufferSizeAdjusted);
        
        /// <summary>The transport received data.</summary>
        public static readonly EventId ReceivedData = GetEventId(TransportEvent.ReceivedData);

        /// <summary>The transport received an invalid datagram message.</summary>
        public static readonly EventId ReceivedInvalidDatagram = GetEventId(TransportEvent.ReceivedInvalidDatagram);

        /// <summary>The transport send buffer size was adjusted.</summary>
        /// <seealso cref="TcpOptions.SendBufferSize"/>
        /// <seealso cref="UdpOptions.SendBufferSize"/>
        public static readonly EventId SendBufferSizeAdjusted = GetEventId(TransportEvent.SendBufferSizeAdjusted);

        /// <summary>The transport sent data.</summary>
        public static readonly EventId SentData = GetEventId(TransportEvent.SentData);

        /// <summary>The transport start accepting connections.</summary>
        public static readonly EventId StartAcceptingConnections =
            GetEventId(TransportEvent.StartAcceptingConnections);

        /// <summary>The transport starts receiving datagram messages.</summary>
        public static readonly EventId StartReceivingDatagrams = GetEventId(TransportEvent.StartReceivingDatagrams);

        /// <summary>The transport failed to start receiving datagram messages.</summary>
        public static readonly EventId StartReceivingDatagramsFailed =
            GetEventId(TransportEvent.StartReceivingDatagramsFailed);

        /// <summary>The transport starts sending datagram messages.</summary>
        public static readonly EventId StartSendingDatagrams = GetEventId(TransportEvent.StartSendingDatagrams);

        /// <summary>The transport failed to start sending datagram messages.</summary>
        public static readonly EventId StartSendingDatagramsFailed =
            GetEventId(TransportEvent.StartSendingDatagramsFailed);

        /// <summary>The transport stops accepting connections.</summary>
        public static readonly EventId StopAcceptingConnections = GetEventId(TransportEvent.StopAcceptingConnections);

        /// <summary>The transport stops receiving datagram messages.</summary>
        public static readonly EventId StopReceivingDatagrams = GetEventId(TransportEvent.StopReceivingDatagrams);

        private const int BaseEventId = Internal.LoggerExtensions.TransportBaseEventId;

        enum TransportEvent
        {
            AcceptingConnectionFailed = BaseEventId,
            ConnectionAccepted,
            ConnectionAcceptFailed,
            ConnectionEventHandlerException,
            ConnectionClosed,
            ConnectionConnectFailed,
            ConnectionEstablished,
            ReceiveBufferSizeAdjusted,
            ReceivedData,
            ReceivedInvalidDatagram,
            SendBufferSizeAdjusted,
            SentData,
            StartAcceptingConnections,
            StartReceivingDatagrams,
            StartReceivingDatagramsFailed,
            StartSendingDatagrams,
            StartSendingDatagramsFailed,
            StopAcceptingConnections,
            StopReceivingDatagrams
        }

        private static EventId GetEventId(TransportEvent e) => new((int)e, e.ToString());
    }
}
