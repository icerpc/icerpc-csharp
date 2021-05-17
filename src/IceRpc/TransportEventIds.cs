// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for transport logging.</summary>
    public static class TransportEventIds
    {
        public static readonly EventId AcceptingConnectionFailed =
            GetEventId(TransportEvent.AcceptingConnectionFailed);
        public static readonly EventId ConnectionAccepted = GetEventId(TransportEvent.ConnectionAccepted);
        public static readonly EventId ConnectionAcceptFailed = GetEventId(TransportEvent.ConnectionAcceptFailed);
        public static readonly EventId ConnectionEventHandlerException =
            GetEventId(TransportEvent.ConnectionEventHandlerException);
        public static readonly EventId ConnectionClosed = GetEventId(TransportEvent.ConnectionClosed);
        public static readonly EventId ConnectionConnectFailed = GetEventId(TransportEvent.ConnectionConnectFailed);
        public static readonly EventId ConnectionEstablished = GetEventId(TransportEvent.ConnectionEstablished);
        public static readonly EventId ReceiveBufferSizeAdjusted =
            GetEventId(TransportEvent.ReceiveBufferSizeAdjusted);
        public static readonly EventId ReceivedData = GetEventId(TransportEvent.ReceivedData);
        public static readonly EventId ReceivedInvalidDatagram = GetEventId(TransportEvent.ReceivedInvalidDatagram);
        public static readonly EventId SendBufferSizeAdjusted = GetEventId(TransportEvent.SendBufferSizeAdjusted);
        public static readonly EventId SentData = GetEventId(TransportEvent.SentData);
        public static readonly EventId StartAcceptingConnections =
            GetEventId(TransportEvent.StartAcceptingConnections);
        public static readonly EventId StartReceivingDatagrams = GetEventId(TransportEvent.StartReceivingDatagrams);
        public static readonly EventId StartReceivingDatagramsFailed =
            GetEventId(TransportEvent.StartReceivingDatagramsFailed);
        public static readonly EventId StartSendingDatagrams = GetEventId(TransportEvent.StartSendingDatagrams);
        public static readonly EventId StartSendingDatagramsFailed =
            GetEventId(TransportEvent.StartSendingDatagramsFailed);
        public static readonly EventId StopAcceptingConnections = GetEventId(TransportEvent.StopAcceptingConnections);
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
