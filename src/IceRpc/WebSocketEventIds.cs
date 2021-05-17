// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for WebSocket logging.</summary>
    public static class WebSocketEventIds
    {
        public static readonly EventId HttpUpgradeRequestAccepted =
            GetEventId(WebSocketEvent.HttpUpgradeRequestAccepted);
        public static readonly EventId HttpUpgradeRequestFailed = GetEventId(WebSocketEvent.HttpUpgradeRequestFailed);
        public static readonly EventId HttpUpgradeRequestSucceed =
            GetEventId(WebSocketEvent.HttpUpgradeRequestSucceed);
        public static readonly EventId ReceivedWebSocketFrame = GetEventId(WebSocketEvent.ReceivedWebSocketFrame);
        public static readonly EventId SendingWebSocketFrame = GetEventId(WebSocketEvent.SendingWebSocketFrame);

        private const int BaseEventId = Internal.LoggerExtensions.WebSocketBaseEventId;
        private enum WebSocketEvent
        {
            HttpUpgradeRequestAccepted = BaseEventId,
            HttpUpgradeRequestFailed,
            HttpUpgradeRequestSucceed,
            ReceivedWebSocketFrame,
            SendingWebSocketFrame
        }

        private static EventId GetEventId(WebSocketEvent e) => new((int)e, e.ToString());
    }
}
