// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace IceRpc.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging WebSocket transport messages.</summary>
    internal static class WebSocketLoggerExtensions
    {
        private static readonly Action<ILogger, Exception> _httpUpgradeRequestAccepted = LoggerMessage.Define(
            LogLevel.Trace,
            WebSocketEventIds.HttpUpgradeRequestAccepted,
            "accepted connection HTTP upgrade request");

        private static readonly Action<ILogger, Exception> _httpUpgradeRequestFailed = LoggerMessage.Define(
            LogLevel.Trace,
            WebSocketEventIds.HttpUpgradeRequestFailed,
            "connection HTTP upgrade request failed");

        private static readonly Action<ILogger, Exception> _httpUpgradeRequestSucceed = LoggerMessage.Define(
            LogLevel.Trace,
            WebSocketEventIds.HttpUpgradeRequestSucceed,
            "connection HTTP upgrade request succeed");

        private static readonly Action<ILogger, string, int, Exception> _receivedWebSocketFrame =
            LoggerMessage.Define<string, int>(
                LogLevel.Trace,
                WebSocketEventIds.ReceivedWebSocketFrame,
                "received {OpCode} frame with {Size} bytes payload");

        private static readonly Action<ILogger, string, int, Exception> _sendingWebSocketFrame =
            LoggerMessage.Define<string, int>(
                LogLevel.Trace,
                WebSocketEventIds.SendingWebSocketFrame,
                "sending {OpCode} frame with {Size} bytes payload");

        internal static void LogHttpUpgradeRequestAccepted(this ILogger logger) =>
            _httpUpgradeRequestAccepted(logger, null!);

        internal static void LogHttpUpgradeRequestFailed(this ILogger logger, Exception ex) =>
            _httpUpgradeRequestFailed(logger, ex);

        internal static void LogHttpUpgradeRequestSucceed(this ILogger logger) =>
            _httpUpgradeRequestSucceed(logger, null!);

        internal static void LogReceivedWebSocketFrame(this ILogger logger, WSSocket.OpCode opCode, int size) =>
            _receivedWebSocketFrame(logger, opCode.ToString(), size, null!);

        internal static void LogSendingWebSocketFrame(this ILogger logger, WSSocket.OpCode opCode, int size) =>
            _sendingWebSocketFrame(logger, opCode.ToString(), size, null!);
    }
}
