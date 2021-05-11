// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for WebSocket logging.</summary>
    public static class WebSocketEventIds
    {
        public static readonly EventId HttpUpgradeRequestAccepted =
            new(BaseEventId + 0, nameof(HttpUpgradeRequestAccepted));
        public static readonly EventId HttpUpgradeRequestFailed =
            new(BaseEventId + 1, nameof(HttpUpgradeRequestFailed));
        public static readonly EventId HttpUpgradeRequestSucceed =
            new(BaseEventId + 2, nameof(HttpUpgradeRequestSucceed));
        public static readonly EventId ReceivedWebSocketFrame =
            new(BaseEventId + 3, nameof(ReceivedWebSocketFrame));
        public static readonly EventId SendingWebSocketFrame =
            new(BaseEventId + 4, nameof(SendingWebSocketFrame));

        private const int BaseEventId = Internal.LoggerExtensions.WebSocketBaseEventId;
    }
}
