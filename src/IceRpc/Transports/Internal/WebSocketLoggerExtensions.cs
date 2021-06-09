// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace IceRpc.Transports.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging WebSocket transport messages.</summary>
    internal static partial class WebSocketLoggerExtensions
    {
        [LoggerMessage(
            EventId = (int)WebSocketEvent.HttpUpgradeRequestAccepted,
            EventName = nameof(WebSocketEvent.HttpUpgradeRequestAccepted),
            Level = LogLevel.Trace,
            Message = "accepted connection HTTP upgrade request")]
        internal static partial void LogHttpUpgradeRequestAccepted(this ILogger logger);

        [LoggerMessage(
            EventId = (int)WebSocketEvent.HttpUpgradeRequestFailed,
            EventName = nameof(WebSocketEvent.HttpUpgradeRequestFailed),
            Level = LogLevel.Trace,
            Message = "connection HTTP upgrade request failed")]
        internal static partial void LogHttpUpgradeRequestFailed(this ILogger logger, Exception ex);

        [LoggerMessage(
            EventId = (int)WebSocketEvent.HttpUpgradeRequestSucceed,
            EventName = nameof(WebSocketEvent.HttpUpgradeRequestSucceed),
            Level = LogLevel.Trace,
            Message = "connection HTTP upgrade request succeed")]
        internal static partial void LogHttpUpgradeRequestSucceed(this ILogger logger);

        [LoggerMessage(
            EventId = (int)WebSocketEvent.ReceivedWebSocketFrame,
            EventName = nameof(WebSocketEvent.ReceivedWebSocketFrame),
            Level = LogLevel.Trace,
            Message = "received {OpCode} frame with {Size} bytes payload")]
        internal static partial void LogReceivedWebSocketFrame(this ILogger logger, WSSocket.OpCode opCode, int size);

        [LoggerMessage(
            EventId = (int)WebSocketEvent.SendingWebSocketFrame,
            EventName = nameof(WebSocketEvent.SendingWebSocketFrame),
            Level = LogLevel.Trace,
            Message = "sending {OpCode} frame with {Size} bytes payload")]
        internal static partial void LogSendingWebSocketFrame(this ILogger logger, WSSocket.OpCode opCode, int size);
    }
}
