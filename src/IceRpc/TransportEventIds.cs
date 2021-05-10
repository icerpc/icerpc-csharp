// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains constants used for transport logging event Ids.</summary>
    public static class TransportEventIds
    {
        public static readonly EventId AcceptingConnectionFailed =
            new(BaseEventId + 0, nameof(AcceptingConnectionFailed));
        public static readonly EventId ConnectionAccepted = new(BaseEventId + 1, nameof(ConnectionAccepted));
        public static readonly EventId ConnectionAcceptFailed = new(BaseEventId + 2, nameof(ConnectionAcceptFailed));
        public static readonly EventId ConnectionEventHandlerException =
            new(BaseEventId + 3, nameof(ConnectionEventHandlerException));
        public static readonly EventId ConnectionClosed = new(BaseEventId + 4, nameof(ConnectionClosed));
        public static readonly EventId ConnectionConnectFailed = new(BaseEventId + 5, nameof(ConnectionConnectFailed));
        public static readonly EventId ConnectionEstablished = new(BaseEventId + 6, nameof(ConnectionEstablished));
        public static readonly EventId ReceiveBufferSizeAdjusted = new(BaseEventId + 7, nameof(ReceiveBufferSizeAdjusted));
        public static readonly EventId ReceivedData = new(BaseEventId + 8, nameof(ReceivedData));
        public static readonly EventId ReceivedInvalidDatagram = new(BaseEventId + 9, nameof(ReceivedInvalidDatagram));
        public static readonly EventId SendBufferSizeAdjusted = new(BaseEventId + 10, nameof(SendBufferSizeAdjusted));
        public static readonly EventId SentData = new(BaseEventId + 11, nameof(SentData));
        public static readonly EventId StartAcceptingConnections =
            new(BaseEventId + 12, nameof(StartAcceptingConnections));
        public static readonly EventId StartReceivingDatagrams =
            new(BaseEventId + 13, nameof(StartReceivingDatagrams));
        public static readonly EventId StartReceivingDatagramsFailed =
            new(BaseEventId + 14, nameof(StartReceivingDatagramsFailed));
        public static readonly EventId StartSendingDatagrams = new(BaseEventId + 15, nameof(StartSendingDatagrams));
        public static readonly EventId StartSendingDatagramsFailed =
            new(BaseEventId + 16, nameof(StartSendingDatagramsFailed));
        public static readonly EventId StopAcceptingConnections =
            new(BaseEventId + 17, nameof(StopAcceptingConnections));
        public static readonly EventId StopReceivingDatagrams = new(BaseEventId + 18, nameof(StopReceivingDatagrams));

        internal const int BaseEventId = Internal.LoggerExtensions.TransportBaseEventId;
    }
}
