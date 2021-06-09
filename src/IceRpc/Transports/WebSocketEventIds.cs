// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>This class contains event ID constants used for WebSocket logging.</summary>
    public enum WebSocketEvent
    {
        /// <summary>An HTTP upgrade request was accepted.</summary>
        HttpUpgradeRequestAccepted = IceRpc.Internal.LoggerExtensions.WebSocketBaseEventId,
        /// <summary>An HTTP upgrade request failed.</summary>
        HttpUpgradeRequestFailed,
        /// <summary>An HTTP upgrade request succeed.</summary>
        HttpUpgradeRequestSucceed,
        /// <summary>A WebSocket frame was received.</summary>
        ReceivedWebSocketFrame,
        /// <summary>A WebSocket frame is being sent.</summary>
        SendingWebSocketFrame
    }
}
