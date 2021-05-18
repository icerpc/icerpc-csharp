// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for WebSocket logging.</summary>
    public static class WebSocketEventIds
    {
        /// <summary>An HTTP upgrade request was accepted.</summary>
        public static readonly EventId HttpUpgradeRequestAccepted =
            GetEventId(WebSocketEvent.HttpUpgradeRequestAccepted);

        /// <summary>An HTTP upgrade request failed.</summary>
        public static readonly EventId HttpUpgradeRequestFailed = GetEventId(WebSocketEvent.HttpUpgradeRequestFailed);

        /// <summary>An HTTP upgrade request succeed.</summary>
        public static readonly EventId HttpUpgradeRequestSucceed =
            GetEventId(WebSocketEvent.HttpUpgradeRequestSucceed);

        /// <summary>A WebSocket frame was received.</summary>
        public static readonly EventId ReceivedWebSocketFrame = GetEventId(WebSocketEvent.ReceivedWebSocketFrame);

        /// <summary>A WebSocket frame is being sent.</summary>
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
