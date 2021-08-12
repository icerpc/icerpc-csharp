// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>This class contains constants used for protocol logging event Ids.</summary>
    internal static partial class ProtocolLoggerExtensions
    {
        [LoggerMessage(
            EventId = (int)ProtocolEvent.DatagramConnectionReceiveCloseConnectionFrame,
            EventName = nameof(ProtocolEvent.DatagramConnectionReceiveCloseConnectionFrame),
            Level = LogLevel.Debug,
            Message = "ignoring close connection frame for datagram connection")]
        internal static partial void LogDatagramConnectionReceiveCloseConnectionFrame(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.DatagramMaximumSizeExceeded,
            EventName = nameof(ProtocolEvent.DatagramMaximumSizeExceeded),
            Level = LogLevel.Debug,
            Message = "maximum datagram size of {Size} exceeded")]
        internal static partial void LogDatagramMaximumSizeExceeded(this ILogger logger, int size);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.DatagramSizeExceededIncomingFrameMaxSize,
            EventName = nameof(ProtocolEvent.DatagramSizeExceededIncomingFrameMaxSize),
            Level = LogLevel.Debug,
            Message = "frame with {Size} bytes exceeds IncomingFrameMaxSize connection option value")]
        internal static partial void LogDatagramSizeExceededIncomingFrameMaxSize(this ILogger logger, int size);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.ReceivedGoAwayCanceledFrame,
            EventName = nameof(ProtocolEvent.ReceivedGoAwayCanceledFrame),
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
            EventId = (int)ProtocolEvent.ReceivedIce1RequestBatchFrame,
            EventName = nameof(ProtocolEvent.ReceivedIce1RequestBatchFrame),
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
            EventId = (int)ProtocolEvent.ReceivedRequestFrame,
            EventName = nameof(ProtocolEvent.ReceivedRequestFrame),
            Level = LogLevel.Information,
            Message = "received request (Path={Path}, Operation={Operation}, PayloadSize={PayloadSize}, " +
                      "PayloadEncoding={PayloadEncoding})")]
        internal static partial void LogReceivedRequest(
            this ILogger logger,
            string path,
            string operation,
            int payloadSize,
            Encoding payloadEncoding);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.ReceivedResponseFrame,
            EventName = nameof(ProtocolEvent.ReceivedResponseFrame),
            Level = LogLevel.Information,
            Message = "received response (ResultType={ResultType})")]
        internal static partial void LogReceivedResponse(this ILogger logger, ResultType resultType);

        [LoggerMessage(
           EventId = (int)ProtocolEvent.RequestException,
           EventName = nameof(ProtocolEvent.RequestException),
           Level = LogLevel.Information,
           Message = "request exception (Path={Path}, Operation={Operation})")]
        internal static partial void LogRequestException(
           this ILogger logger,
           string path,
           string operation,
           Exception ex);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.RetryRequestConnectionException,
            EventName = nameof(ProtocolEvent.RetryRequestConnectionException),
            Level = LogLevel.Debug,
            Message = "retrying request because of connection exception (Path={Path}, Operation={Operation}, " +
                      "RetryPolicy={RetryPolicy}, Attempt={Attempt}/{MaxAttempts})")]
        internal static partial void LogRetryRequestConnectionException(
            this ILogger logger,
            string path,
            string operation,
            RetryPolicy retryPolicy,
            int attempt,
            int maxAttempts,
            Exception? ex);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.RetryRequestRetryableException,
            EventName = nameof(ProtocolEvent.RetryRequestRetryableException),
            Level = LogLevel.Debug,
            Message = "retrying request because of retryable exception (Path={Path}, Operation={Operation}, " +
                      "RetryPolicy={RetryPolicy}, Attempt={Attempt}/{MaxAttempts})")]
        internal static partial void LogRetryRequestRetryableException(
            this ILogger logger,
            string path,
            string operation,
            RetryPolicy retryPolicy,
            int attempt,
            int maxAttempts,
            Exception? ex);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.SentGoAwayCanceledFrame,
            EventName = nameof(ProtocolEvent.SentGoAwayCanceledFrame),
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
            EventId = (int)ProtocolEvent.SentRequestFrame,
            EventName = nameof(ProtocolEvent.SentRequestFrame),
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
            EventId = (int)ProtocolEvent.SentResponseFrame,
            EventName = nameof(ProtocolEvent.SentResponseFrame),
            Level = LogLevel.Information,
            Message = "sent response (ResultType={ResultType}, PayloadSize={PayloadSize}, " +
                      "PayloadEncoding={PayloadEncoding})")]
        internal static partial void LogSentResponse(
            this ILogger logger,
            ResultType resultType,
            int payloadSize,
            Encoding payloadEncoding);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.ReceivedGoAwayFrame,
            EventName = nameof(ProtocolEvent.ReceivedGoAwayFrame),
            Level = LogLevel.Debug,
            Message = "received go away frame (LastBidirectionalStreamId={LastBidirectionalStreamId}, " +
                      "LastUnidirectionalStreamId={LastUnidirectionalStreamId}, Reason={Reason})")]
        private static partial void LogReceivedGoAwayFrame(
            this ILogger logger,
            long lastBidirectionalStreamId,
            long lastUnidirectionalStreamId,
            string reason);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.ReceivedIce1CloseConnectionFrame,
            EventName = nameof(ProtocolEvent.ReceivedIce1CloseConnectionFrame),
            Level = LogLevel.Debug,
            Message = "received close connection frame")]
        private static partial void LogReceivedIce1CloseConnectionFrame(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.ReceivedIce1ValidateConnectionFrame,
            EventName = nameof(ProtocolEvent.ReceivedIce1ValidateConnectionFrame),
            Level = LogLevel.Debug,
            Message = "received validate connection frame")]
        private static partial void LogReceivedIce1ValidateConnectionFrame(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.ReceivedInitializeFrame,
            EventName = nameof(ProtocolEvent.ReceivedInitializeFrame),
            Level = LogLevel.Debug,
            Message = "received initialize frame (IncomingFrameMaxSize={IncomingFrameMaxSize})")]
        private static partial void LogReceivedInitializeFrame(this ILogger logger, int incomingFrameMaxSize);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.SentGoAwayFrame,
            EventName = nameof(ProtocolEvent.SentGoAwayFrame),
            Level = LogLevel.Debug,
            Message = "sent go away frame (LastBidirectionalStreamId={LastBidirectionalStreamId}, " +
                      "LastUnidirectionalStreamId={LastUnidirectionalStreamId}, Reason={Reason})")]
        private static partial void LogSentGoAwayFrame(
            this ILogger logger,
            long lastBidirectionalStreamId,
            long lastUnidirectionalStreamId,
            string reason);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.SentIce1CloseConnectionFrame,
            EventName = nameof(ProtocolEvent.SentIce1CloseConnectionFrame),
            Level = LogLevel.Debug,
            Message = "sent close connection frame (Reason={Reason})")]
        private static partial void LogSentIce1CloseConnectionFrame(this ILogger logger, string reason);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.SentIce1ValidateConnectionFrame,
            EventName = nameof(ProtocolEvent.SentIce1ValidateConnectionFrame),
            Level = LogLevel.Debug,
            Message = "sent validate connection frame")]
        private static partial void LogSentIce1ValidateConnectionFrame(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ProtocolEvent.SentInitializeFrame,
            EventName = nameof(ProtocolEvent.SentInitializeFrame),
            Level = LogLevel.Debug,
            Message = "sent initialize frame (IncomingFrameMaxSize={IncomingFrameMaxSize})")]
        private static partial void LogSentInitializeFrame(this ILogger logger, int incomingFrameMaxSize);
    }
}
