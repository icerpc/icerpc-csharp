// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>This class contains the ILogger extension methods for logging protocol messages.</summary>
    // TODO: split into IceLoggerExtensions and IceRpcLoggerExtensions.
    internal static partial class ProtocolLoggerExtensions
    {
        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedIceRequestBatchFrame,
            EventName = nameof(ProtocolEventIds.ReceivedIceRequestBatchFrame),
            Level = LogLevel.Debug,
            Message = "received batch request (RequestCount={RequestCount})")]
        internal static partial void LogReceivedIceRequestBatchFrame(this ILogger logger, int requestCount);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedGoAwayFrame,
            EventName = nameof(ProtocolEventIds.ReceivedGoAwayFrame),
            Level = LogLevel.Debug,
            Message = "received go away frame (LastBidirectionalStreamId={LastBidirectionalStreamId}, " +
                      "LastUnidirectionalStreamId={LastUnidirectionalStreamId}, Reason={Reason})")]
        internal static partial void LogReceivedGoAwayFrame(
            this ILogger logger,
            long lastBidirectionalStreamId,
            long lastUnidirectionalStreamId,
            string reason);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedIceCloseConnectionFrame,
            EventName = nameof(ProtocolEventIds.ReceivedIceCloseConnectionFrame),
            Level = LogLevel.Debug,
            Message = "received close connection frame")]
        internal static partial void LogReceivedIceCloseConnectionFrame(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedIceValidateConnectionFrame,
            EventName = nameof(ProtocolEventIds.ReceivedIceValidateConnectionFrame),
            Level = LogLevel.Debug,
            Message = "received validate connection frame")]
        internal static partial void LogReceivedIceValidateConnectionFrame(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.SentGoAwayFrame,
            EventName = nameof(ProtocolEventIds.SentGoAwayFrame),
            Level = LogLevel.Debug,
            Message = "sent go away frame (LastBidirectionalStreamId={LastBidirectionalStreamId}, " +
                      "LastUnidirectionalStreamId={LastUnidirectionalStreamId}, Reason={Reason})")]
        internal static partial void LogSentGoAwayFrame(
            this ILogger logger,
            long lastBidirectionalStreamId,
            long lastUnidirectionalStreamId,
            string reason);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.SentIceCloseConnectionFrame,
            EventName = nameof(ProtocolEventIds.SentIceCloseConnectionFrame),
            Level = LogLevel.Debug,
            Message = "sent close connection frame (Reason={Reason})")]
        internal static partial void LogSentIceCloseConnectionFrame(this ILogger logger, string reason);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.SentIceValidateConnectionFrame,
            EventName = nameof(ProtocolEventIds.SentIceValidateConnectionFrame),
            Level = LogLevel.Debug,
            Message = "sent validate connection frame")]
        internal static partial void LogSentIceValidateConnectionFrame(this ILogger logger);
    }
}
