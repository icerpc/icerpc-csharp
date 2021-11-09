// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>This class contains the ILogger extension methods for logging protocol messages.</summary>
    // TODO: split into Ice1LoggerExtensions and Ice2LoggerExtensions.
    internal static partial class ProtocolLoggerExtensions
    {
        [LoggerMessage(
            EventId = (int)ProtocolEventIds.DatagramConnectionReceiveCloseConnectionFrame,
            EventName = nameof(ProtocolEventIds.DatagramConnectionReceiveCloseConnectionFrame),
            Level = LogLevel.Debug,
            Message = "ignoring close connection frame for datagram connection")]
        internal static partial void LogDatagramConnectionReceiveCloseConnectionFrame(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.DatagramMaximumSizeExceeded,
            EventName = nameof(ProtocolEventIds.DatagramMaximumSizeExceeded),
            Level = LogLevel.Debug,
            Message = "maximum datagram size of {Size} exceeded")]
        internal static partial void LogDatagramMaximumSizeExceeded(this ILogger logger, int size);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.DatagramSizeExceededIncomingFrameMaxSize,
            EventName = nameof(ProtocolEventIds.DatagramSizeExceededIncomingFrameMaxSize),
            Level = LogLevel.Debug,
            Message = "frame with {Size} bytes exceeds IncomingFrameMaxSize connection option value")]
        internal static partial void LogDatagramSizeExceededIncomingFrameMaxSize(this ILogger logger, int size);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedIce1RequestBatchFrame,
            EventName = nameof(ProtocolEventIds.ReceivedIce1RequestBatchFrame),
            Level = LogLevel.Debug,
            Message = "received batch request (RequestCount={RequestCount})")]
        internal static partial void LogReceivedIce1RequestBatchFrame(this ILogger logger, int requestCount);

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
            EventId = (int)ProtocolEventIds.ReceivedIce1CloseConnectionFrame,
            EventName = nameof(ProtocolEventIds.ReceivedIce1CloseConnectionFrame),
            Level = LogLevel.Debug,
            Message = "received close connection frame")]
        internal static partial void LogReceivedIce1CloseConnectionFrame(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedIce1ValidateConnectionFrame,
            EventName = nameof(ProtocolEventIds.ReceivedIce1ValidateConnectionFrame),
            Level = LogLevel.Debug,
            Message = "received validate connection frame")]
        internal static partial void LogReceivedIce1ValidateConnectionFrame(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedInitializeFrame,
            EventName = nameof(ProtocolEventIds.ReceivedInitializeFrame),
            Level = LogLevel.Debug,
            Message = "received initialize frame (IncomingFrameMaxSize={IncomingFrameMaxSize})")]
        internal static partial void LogReceivedInitializeFrame(this ILogger logger, int incomingFrameMaxSize);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedInvalidDatagram,
            EventName = nameof(ProtocolEventIds.ReceivedInvalidDatagram),
            Level = LogLevel.Debug,
            Message = "received invalid {Bytes} bytes datagram")]
        internal static partial void LogReceivedInvalidDatagram(this ILogger logger, int bytes);

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
            EventId = (int)ProtocolEventIds.SentIce1CloseConnectionFrame,
            EventName = nameof(ProtocolEventIds.SentIce1CloseConnectionFrame),
            Level = LogLevel.Debug,
            Message = "sent close connection frame (Reason={Reason})")]
        internal static partial void LogSentIce1CloseConnectionFrame(this ILogger logger, string reason);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.SentIce1ValidateConnectionFrame,
            EventName = nameof(ProtocolEventIds.SentIce1ValidateConnectionFrame),
            Level = LogLevel.Debug,
            Message = "sent validate connection frame")]
        internal static partial void LogSentIce1ValidateConnectionFrame(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.SentInitializeFrame,
            EventName = nameof(ProtocolEventIds.SentInitializeFrame),
            Level = LogLevel.Debug,
            Message = "sent initialize frame (IncomingFrameMaxSize={IncomingFrameMaxSize})")]
        internal static partial void LogSentInitializeFrame(this ILogger logger, int incomingFrameMaxSize);
    }
}
