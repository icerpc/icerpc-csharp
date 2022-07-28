// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Internal;

/// <summary>This class contains the ILogger extension methods for logging protocol messages.</summary>
// TODO: split into IceLoggerExtensions and IceRpcLoggerExtensions.
internal static partial class ProtocolLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)ProtocolEventId.ReceivedIceRequestBatchFrame,
        EventName = nameof(ProtocolEventId.ReceivedIceRequestBatchFrame),
        Level = LogLevel.Debug,
        Message = "received batch request (RequestCount={RequestCount})")]
    internal static partial void LogReceivedIceRequestBatchFrame(this ILogger logger, int requestCount);

    [LoggerMessage(
        EventId = (int)ProtocolEventId.ReceivedGoAwayFrame,
        EventName = nameof(ProtocolEventId.ReceivedGoAwayFrame),
        Level = LogLevel.Debug,
        Message = "received go away frame (LastBidirectionalStreamId={LastBidirectionalStreamId}, " +
                  "LastUnidirectionalStreamId={LastUnidirectionalStreamId}, Reason={Reason})")]
    internal static partial void LogReceivedGoAwayFrame(
        this ILogger logger,
        long lastBidirectionalStreamId,
        long lastUnidirectionalStreamId,
        string reason);

    [LoggerMessage(
        EventId = (int)ProtocolEventId.ReceivedIceCloseConnectionFrame,
        EventName = nameof(ProtocolEventId.ReceivedIceCloseConnectionFrame),
        Level = LogLevel.Debug,
        Message = "received close connection frame")]
    internal static partial void LogReceivedIceCloseConnectionFrame(this ILogger logger);

    [LoggerMessage(
        EventId = (int)ProtocolEventId.ReceivedIceValidateConnectionFrame,
        EventName = nameof(ProtocolEventId.ReceivedIceValidateConnectionFrame),
        Level = LogLevel.Debug,
        Message = "received validate connection frame")]
    internal static partial void LogReceivedIceValidateConnectionFrame(this ILogger logger);

    [LoggerMessage(
        EventId = (int)ProtocolEventId.SentGoAwayFrame,
        EventName = nameof(ProtocolEventId.SentGoAwayFrame),
        Level = LogLevel.Debug,
        Message = "sent go away frame (LastBidirectionalStreamId={LastBidirectionalStreamId}, " +
                  "LastUnidirectionalStreamId={LastUnidirectionalStreamId}, Reason={Reason})")]
    internal static partial void LogSentGoAwayFrame(
        this ILogger logger,
        long lastBidirectionalStreamId,
        long lastUnidirectionalStreamId,
        string reason);

    [LoggerMessage(
        EventId = (int)ProtocolEventId.SentIceCloseConnectionFrame,
        EventName = nameof(ProtocolEventId.SentIceCloseConnectionFrame),
        Level = LogLevel.Debug,
        Message = "sent close connection frame (Reason={Reason})")]
    internal static partial void LogSentIceCloseConnectionFrame(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = (int)ProtocolEventId.SentIceValidateConnectionFrame,
        EventName = nameof(ProtocolEventId.SentIceValidateConnectionFrame),
        Level = LogLevel.Debug,
        Message = "sent validate connection frame")]
    internal static partial void LogSentIceValidateConnectionFrame(this ILogger logger);
}
