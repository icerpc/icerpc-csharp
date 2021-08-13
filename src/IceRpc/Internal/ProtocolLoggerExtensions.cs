// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>This class contains the ILogger extension methods for logging protocol messages.</summary>
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
            EventId = (int)ProtocolEventIds.ReceivedGoAwayCanceledFrame,
            EventName = nameof(ProtocolEventIds.ReceivedGoAwayCanceledFrame),
            Level = LogLevel.Debug,
            Message = "received go away canceled frame")]
        internal static partial void LogReceivedGoAwayCanceledFrame(this ILogger logger);

        internal static void LogReceivedGoAwayFrame(
            this ILogger logger,
            MultiStreamConnection connection,
            long lastBidirectionalId,
            long lastUnidirectionalId,
            string message)
        {
            if (connection.Protocol == Protocol.Ice1)
            {
                logger.LogReceivedIce1CloseConnectionFrame();
            }
            else
            {
                logger.LogReceivedGoAwayFrame(lastBidirectionalId, lastUnidirectionalId, message);
            }
        }

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedIce1RequestBatchFrame,
            EventName = nameof(ProtocolEventIds.ReceivedIce1RequestBatchFrame),
            Level = LogLevel.Information,
            Message = "received batch request (RequestCount={RequestCount})")]
        internal static partial void LogReceivedIce1RequestBatchFrame(this ILogger logger, int requestCount);

        internal static void LogReceivedInitializeFrame(this ILogger logger, MultiStreamConnection connection)
        {
            if (connection.Protocol == Protocol.Ice1)
            {
                logger.LogReceivedIce1ValidateConnectionFrame();
            }
            else
            {
                logger.LogReceivedInitializeFrame(connection.PeerIncomingFrameMaxSize!.Value);
            }
        }

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.SentGoAwayCanceledFrame,
            EventName = nameof(ProtocolEventIds.SentGoAwayCanceledFrame),
            Level = LogLevel.Debug,
            Message = "sent go away canceled frame")]
        internal static partial void LogSentGoAwayCanceledFrame(this ILogger logger);

        internal static void LogSentGoAwayFrame(
            this ILogger logger,
            MultiStreamConnection connection,
            long lastBidirectionalId,
            long lastUnidirectionalId,
            string message)
        {
            if (connection.Protocol == Protocol.Ice1)
            {
                logger.LogSentIce1CloseConnectionFrame(message);
            }
            else
            {
                logger.LogSentGoAwayFrame(lastBidirectionalId, lastUnidirectionalId, message);
            }
        }

        internal static void LogSentInitializeFrame(
            this ILogger logger,
            MultiStreamConnection connection,
            int incomingFrameMaxSize)
        {
            if (connection.Protocol == Protocol.Ice1)
            {
                logger.LogSentIce1ValidateConnectionFrame();
            }
            else
            {
                logger.LogSentInitializeFrame(incomingFrameMaxSize);
            }
        }

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.SentRequestFrame,
            EventName = nameof(ProtocolEventIds.SentRequestFrame),
            Level = LogLevel.Information,
            Message = "sent request (Path={Path}, Operation={Operation}, PayloadSize={PayloadSize}, " +
                      "PayloadEncoding={PayloadEncoding})")]
        internal static partial void LogSentRequest(
            this ILogger logger,
            string path,
            string operation,
            int payloadSize,
            Encoding payloadEncoding);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedGoAwayFrame,
            EventName = nameof(ProtocolEventIds.ReceivedGoAwayFrame),
            Level = LogLevel.Debug,
            Message = "received go away frame (LastBidirectionalStreamId={LastBidirectionalStreamId}, " +
                      "LastUnidirectionalStreamId={LastUnidirectionalStreamId}, Reason={Reason})")]
        private static partial void LogReceivedGoAwayFrame(
            this ILogger logger,
            long lastBidirectionalStreamId,
            long lastUnidirectionalStreamId,
            string reason);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedIce1CloseConnectionFrame,
            EventName = nameof(ProtocolEventIds.ReceivedIce1CloseConnectionFrame),
            Level = LogLevel.Debug,
            Message = "received close connection frame")]
        private static partial void LogReceivedIce1CloseConnectionFrame(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedIce1ValidateConnectionFrame,
            EventName = nameof(ProtocolEventIds.ReceivedIce1ValidateConnectionFrame),
            Level = LogLevel.Debug,
            Message = "received validate connection frame")]
        private static partial void LogReceivedIce1ValidateConnectionFrame(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedInitializeFrame,
            EventName = nameof(ProtocolEventIds.ReceivedInitializeFrame),
            Level = LogLevel.Debug,
            Message = "received initialize frame (IncomingFrameMaxSize={IncomingFrameMaxSize})")]
        private static partial void LogReceivedInitializeFrame(this ILogger logger, int incomingFrameMaxSize);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.SentGoAwayFrame,
            EventName = nameof(ProtocolEventIds.SentGoAwayFrame),
            Level = LogLevel.Debug,
            Message = "sent go away frame (LastBidirectionalStreamId={LastBidirectionalStreamId}, " +
                      "LastUnidirectionalStreamId={LastUnidirectionalStreamId}, Reason={Reason})")]
        private static partial void LogSentGoAwayFrame(
            this ILogger logger,
            long lastBidirectionalStreamId,
            long lastUnidirectionalStreamId,
            string reason);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.SentIce1CloseConnectionFrame,
            EventName = nameof(ProtocolEventIds.SentIce1CloseConnectionFrame),
            Level = LogLevel.Debug,
            Message = "sent close connection frame (Reason={Reason})")]
        private static partial void LogSentIce1CloseConnectionFrame(this ILogger logger, string reason);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.SentIce1ValidateConnectionFrame,
            EventName = nameof(ProtocolEventIds.SentIce1ValidateConnectionFrame),
            Level = LogLevel.Debug,
            Message = "sent validate connection frame")]
        private static partial void LogSentIce1ValidateConnectionFrame(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.SentInitializeFrame,
            EventName = nameof(ProtocolEventIds.SentInitializeFrame),
            Level = LogLevel.Debug,
            Message = "sent initialize frame (IncomingFrameMaxSize={IncomingFrameMaxSize})")]
        private static partial void LogSentInitializeFrame(this ILogger logger, int incomingFrameMaxSize);
    }
}
