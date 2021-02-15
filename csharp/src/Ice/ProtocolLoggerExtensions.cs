// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace ZeroC.Ice
{
    internal static class ProtocolLoggerExtensions
    {
        private static readonly Action<ILogger, Protocol, string, Encoding, int, Exception> _receivedProtocolFrame =
            LoggerMessage.Define<Protocol, string, Encoding, int>(
                LogLevel.Debug,
                GetEventId(ProtocolEvent.ReceivedProtocolFrame),
                "received {Protocol} {FrameType} frame: encoding = {Encoding}, size = {Size}");

        private static readonly Action<ILogger, Protocol, string, Encoding, int, Exception> _sendingProtocolFrame =
            LoggerMessage.Define<Protocol, string, Encoding, int>(
                LogLevel.Debug,
                GetEventId(ProtocolEvent.SendingProtocolFrame),
                "sending {Protocol} {FrameType} frame: encoding = {Encoding}, size = {Size}");

        private static readonly Action<ILogger, string, Exception> _warnProxySecureOptionHasNoEffect =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                GetEventId(ProtocolEvent.WarnProxySecureOptionHasNoEffect),
                "warning while parsing {Proxy}: the -s proxy option no longer has any effect");

        private static readonly Action<ILogger, string, Exception> _warnDeprecatedProperty =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                GetEventId(ProtocolEvent.WarnDeprecatedProperty),
                "deprecated property {Property}");

        internal static void LogReceivedFrame(
            this ILogger logger,
            Protocol protocol,
            Ice1FrameType frameType,
            Encoding encoding,
            int size) =>
            _receivedProtocolFrame(logger, protocol, Ice1FrameTypeToString(frameType), encoding, size, null!);

        internal static void LogReceivedFrame(
            this ILogger logger,
            Protocol protocol,
            Ice2FrameType frameType,
            Encoding encoding,
            int size) =>
            _receivedProtocolFrame(logger, protocol, Ice2FrameTypeToString(frameType), encoding, size, null!);

        internal static void LogSendingFrame(
            this ILogger logger,
            Protocol protocol,
            Ice1FrameType frameType,
            Encoding encoding,
            int size) =>
            _sendingProtocolFrame(logger, protocol, Ice1FrameTypeToString(frameType), encoding, size, null!);

        internal static void LogSendingFrame(
            this ILogger logger,
            Protocol protocol,
            Ice2FrameType frameType,
            Encoding encoding,
            int size) =>
            _sendingProtocolFrame(logger, protocol, Ice2FrameTypeToString(frameType), encoding, size, null!);

        internal static void LogWarnDeprecatedProperty(this ILogger logger, string property) =>
            _warnDeprecatedProperty(logger, property, null!);
        internal static void LogWarnProxySecureOptionHasNoEffect(this ILogger logger, string proxy) =>
            _warnProxySecureOptionHasNoEffect(logger, proxy, null!);

        private static EventId GetEventId(ProtocolEvent e) => new EventId((int)e, e.ToString());

        private static string Ice1FrameTypeToString(Ice1FrameType type) =>
            type switch
            {
                Ice1FrameType.Request => "Request",
                Ice1FrameType.Reply => "Response",
                Ice1FrameType.ValidateConnection => "ValidateConnection",
                Ice1FrameType.CloseConnection => "CloseConnection",
                Ice1FrameType.RequestBatch => "RequestBatch",
                _ => "Unknown"
            };

        private static string Ice2FrameTypeToString(Ice2FrameType type) =>
            type switch
            {
                Ice2FrameType.Request => "Request",
                Ice2FrameType.Response => "Response",
                Ice2FrameType.Initialize => "Initialize",
                Ice2FrameType.GoAway => "GoAway",
                _ => "Unknown"
            };

        private enum ProtocolEvent
        {
            ReceivedProtocolFrame,
            SendingProtocolFrame,

            WarnProxySecureOptionHasNoEffect,
            WarnDeprecatedProperty,
        }
    }
}