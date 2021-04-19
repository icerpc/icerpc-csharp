// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IceRpc
{
    /// <summary>This class contains ILogger extensions methods for logging protocol messages.</summary>
    internal static class ProtocolLoggerExtensions
    {
        // TODO: eliminate gaps in event IDs below.

        private const int BaseEventId = LoggerExtensions.ProtocolBaseEventId;

        private const int DatagramConnectionReceiveCloseConnectionFrame = BaseEventId + 0;
        private const int DatagramSizeExceededIncomingFrameMaxSize = BaseEventId + 1;
        private const int DatagramMaximumSizeExceeded = BaseEventId + 2;

        private const int ReceivedIce1CloseConnectionFrame = BaseEventId + 3;
        private const int ReceivedIce1RequestBatchFrame = BaseEventId + 4;
        private const int ReceivedIce1ValidateConnectionFrame = BaseEventId + 5;

        private const int ReceivedGoAwayFrame = BaseEventId + 6;
        private const int ReceivedInitializeFrame = BaseEventId + 7;
        private const int ReceivedRequestFrame = BaseEventId + 8;
        private const int ReceivedResponseFrame = BaseEventId + 9;
        private const int RequestException = BaseEventId + 11;
        private const int RetryRequestRetryableException = BaseEventId + 12;
        private const int RetryRetryConnectionException = BaseEventId + 13;

        private const int SentIce1ValidateConnectionFrame = BaseEventId + 14;
        private const int SentIce1CloseConnectionFrame = BaseEventId + 15;
        private const int SentGoAwayFrame = BaseEventId + 16;
        private const int SentInitializeFrame = BaseEventId + 17;
        private const int SentRequestFrame = BaseEventId + 18;
        private const int SentResponseFrame = BaseEventId + 19;

        private static readonly Action<ILogger, int, Exception> _datagramMaximumSizeExceeded =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                new EventId(DatagramMaximumSizeExceeded, nameof(DatagramMaximumSizeExceeded)),
                "maximum datagram size of {Size} exceeded");

        private static readonly Action<ILogger, int, Exception> _datagramSizeExceededIncomingFrameMaxSize =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                new EventId(DatagramSizeExceededIncomingFrameMaxSize, nameof(DatagramSizeExceededIncomingFrameMaxSize)),
                "frame with {Size} bytes exceeds IncomingFrameMaxSize connection option value");

        private static readonly Action<ILogger, Exception> _datagramConnectionReceiveCloseConnectionFrame =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(
                    DatagramConnectionReceiveCloseConnectionFrame,
                    nameof(DatagramConnectionReceiveCloseConnectionFrame)),
                "ignoring close connection frame for datagram connection");

        private static readonly Action<ILogger, Exception> _receivedIce1CloseConnectionFrame =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(ReceivedIce1CloseConnectionFrame, nameof(ReceivedIce1CloseConnectionFrame)),
                "received close connection frame");

        private static readonly Action<ILogger, int, Exception> _receivedIce1RequestBatchFrame =
            LoggerMessage.Define<int>(
                LogLevel.Information,
                new EventId(ReceivedIce1RequestBatchFrame, nameof(ReceivedIce1RequestBatchFrame)),
                "received batch request (RequestCount={RequestCount}");

        private static readonly Action<ILogger, Exception> _receivedIce1ValidateConnectionFrame =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(ReceivedIce1ValidateConnectionFrame, nameof(ReceivedIce1ValidateConnectionFrame)),
                "received validate connection frame");

        private static readonly Action<ILogger, long, long, string, Exception> _receivedGoAwayFrame =
            LoggerMessage.Define<long, long, string>(
                LogLevel.Debug,
                new EventId(ReceivedGoAwayFrame, nameof(ReceivedGoAwayFrame)),
                "received go away frame (LastBidirectionalStreamId={LastBidirectionalStreamId}, " +
                "LastUnidirectionalStreamId={LastUnidirectionalStreamId}, Reason={Reason})");

        private static readonly Action<ILogger, int, Exception> _receivedInitializeFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(ReceivedInitializeFrame, nameof(ReceivedInitializeFrame)),
            "received initialize frame (IncomingFrameMaxSize={IncomingFrameMaxSize})");

        private static readonly Action<ILogger, string, string, int, Encoding, CompressionFormat, IReadOnlyDictionary<string, string>, Exception> _receivedRequestFrame =
            LoggerMessage.Define<string, string, int, Encoding, CompressionFormat, IReadOnlyDictionary<string, string>>(
                LogLevel.Information,
                new EventId(ReceivedRequestFrame, nameof(ReceivedRequestFrame)),
                "received request (Path={Path}, Operation={Operation}, PayloadSize={PayloadSize}, " +
                "PayloadEncoding={PayloadEncoding}, PayloadCompressionFormat={PayloadCompressionFormat}, " +
                "Context={Context})");

        private static readonly Action<ILogger, ResultType, Exception> _receivedResponseFrame =
            LoggerMessage.Define<ResultType>(
                LogLevel.Information,
                new EventId(ReceivedResponseFrame, nameof(ReceivedResponseFrame)),
                "received response (ResultType={ResultType})");

        private static readonly Action<ILogger, string, string, Exception> _requestException =
            LoggerMessage.Define<string, string>(
                LogLevel.Information,
                new EventId(RequestException, nameof(RequestException)),
                "request exception (Path={Path}, Operation={Operation})");

        private static readonly Action<ILogger, string, string, RetryPolicy, int, int, Exception> _retryRequestRetryableException =
            LoggerMessage.Define<string, string, RetryPolicy, int, int>(
                LogLevel.Debug,
                new EventId(RetryRequestRetryableException, nameof(RetryRequestRetryableException)),
                "retrying request because of retryable exception (Path={Path}, Operation={Operation}, " +
                "RetryPolicy={RetryPolicy}, Attempt={Attempt}/{MaxAttempts})");

        private static readonly Action<ILogger, string, string, RetryPolicy, int, int, Exception> _retryRequestConnectionException =
            LoggerMessage.Define<string, string, RetryPolicy, int, int>(
                LogLevel.Debug,
                new EventId(RetryRetryConnectionException, nameof(RetryRetryConnectionException)),
                "retrying request because of connection exception (Path={Path}, Operation={Operation}, " +
                "RetryPolicy={RetryPolicy}, Attempt={Attempt}/{MaxAttempts})");

        private static readonly Action<ILogger, Exception> _sentIce1ValidateConnectionFrame = LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(SentIce1ValidateConnectionFrame, nameof(SentIce1ValidateConnectionFrame)),
            "sent validate connection frame");

        private static readonly Action<ILogger, string, Exception> _sentIce1CloseConnectionFrame =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(SentIce1CloseConnectionFrame, nameof(SentIce1CloseConnectionFrame)),
                "sent close connection frame (Reason={Reason})");

        private static readonly Action<ILogger, long, long, string, Exception> _sentGoAwayFrame =
            LoggerMessage.Define<long, long, string>(
                LogLevel.Debug,
                new EventId(SentGoAwayFrame, nameof(SentGoAwayFrame)),
                "sent go away frame (LastBidirectionalStreamId={LastBidirectionalStreamId}, " +
                "LastUnidirectionalStreamId={LastUnidirectionalStreamId}, Reason={Reason})");

        private static readonly Action<ILogger, int, Exception> _sentInitializeFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(SentInitializeFrame, nameof(SentInitializeFrame)),
            "sent initialize frame (IncomingFrameMaxSize={IncomingFrameMaxSize})");

        private static readonly Action<ILogger, string, string, int, Encoding, CompressionFormat, IReadOnlyDictionary<string, string>, Exception> _sentRequestFrame =
            LoggerMessage.Define<string, string, int, Encoding, CompressionFormat, IReadOnlyDictionary<string, string>>(
                LogLevel.Information,
                new EventId(SentRequestFrame, nameof(SentRequestFrame)),
                "sent request (Path={Path}, Operation={Operation}, PayloadSize={PayloadSize}, " +
                "PayloadEncoding={PayloadEncoding}, PayloadCompressionFormat={PayloadCompressionFormat}, " +
                "Context={Context})");

        private static readonly Action<ILogger, ResultType, int, Encoding, CompressionFormat, Exception> _sentResponseFrame =
            LoggerMessage.Define<ResultType, int, Encoding, CompressionFormat>(
                LogLevel.Information,
                new EventId(SentResponseFrame, nameof(SentResponseFrame)),
                "sent response (ResultType={ResultType}, Size={Size}, Encoding={Encoding}, " +
                "CompressionFormat={CompressionFormat})");

        internal static void LogDatagramSizeExceededIncomingFrameMaxSize(this ILogger logger, int size) =>
            _datagramSizeExceededIncomingFrameMaxSize(logger, size, null!);

        internal static void LogDatagramConnectionReceiveCloseConnectionFrame(this ILogger logger) =>
            _datagramConnectionReceiveCloseConnectionFrame(logger, null!);

        internal static void LogDatagramMaximumSizeExceeded(this ILogger logger, int bytes) =>
            _datagramMaximumSizeExceeded(logger, bytes, null!);

        internal static void LogReceivedIce1RequestBatchFrame(this ILogger logger, int requests) =>
            _receivedIce1RequestBatchFrame(logger, requests, null!);

        internal static void LogReceivedGoAwayFrame(
            this ILogger logger,
            MultiStreamSocket socket,
            long lastBidirectionalId,
            long lastUnidirectionalId,
            string message)
        {
            if (socket.Endpoint.Protocol == Protocol.Ice1)
            {
                _receivedIce1CloseConnectionFrame(logger, null!);
            }
            else
            {
                _receivedGoAwayFrame(logger, lastBidirectionalId, lastUnidirectionalId, message, null!);
            }
        }

        internal static void LogReceivedInitializeFrame(this ILogger logger, MultiStreamSocket socket)
        {
            if (socket.Endpoint.Protocol == Protocol.Ice1)
            {
                _receivedIce1ValidateConnectionFrame(logger, null!);
            }
            else
            {
                _receivedInitializeFrame(logger, socket.PeerIncomingFrameMaxSize!.Value, null!);
            }
        }

        internal static void LogReceivedRequest(this ILogger logger, IncomingRequestFrame request) =>
            _receivedRequestFrame(
                logger,
                request.Path,
                request.Operation,
                request.PayloadSize,
                request.PayloadEncoding,
                request.PayloadCompressionFormat,
                request.Context,
                null!);

        internal static void LogReceivedResponse(this ILogger logger, IncomingResponseFrame response) =>
            _receivedResponseFrame(logger, response.ResultType, null!);

        internal static void LogRetryRequestRetryableException(
            this ILogger logger,
            RetryPolicy retryPolicy,
            int attempt,
            int maxAttempts,
            OutgoingRequestFrame request,
            Exception? ex) =>
            _retryRequestRetryableException(
                logger,
                request.Path,
                request.Operation,
                retryPolicy,
                attempt,
                maxAttempts,
                ex!);

        internal static void LogRetryRequestConnectionException(
            this ILogger logger,
            RetryPolicy retryPolicy,
            int attempt,
            int maxAttempts,
            OutgoingRequestFrame request,
            Exception? ex) =>
            _retryRequestConnectionException(
                logger,
                request.Path,
                request.Operation,
                retryPolicy,
                attempt,
                maxAttempts,
                ex!);

        internal static void LogRequestException(this ILogger logger, OutgoingRequestFrame request, Exception ex) =>
            _requestException(logger, request.Path, request.Operation, ex);

        internal static void LogSentGoAwayFrame(
            this ILogger logger,
            MultiStreamSocket socket,
            long lastBidirectionalId,
            long lastUnidirectionalId,
            string message)
        {
            if (socket.Endpoint.Protocol == Protocol.Ice1)
            {
                _sentIce1CloseConnectionFrame(logger, message, null!);
            }
            else
            {
                _sentGoAwayFrame(logger, lastBidirectionalId, lastUnidirectionalId, message, null!);
            }
        }

        internal static void LogSentInitializeFrame(
            this ILogger logger,
            MultiStreamSocket socket,
            int incomingFrameMaxSize)
        {
            if (socket.Endpoint.Protocol == Protocol.Ice1)
            {
                _sentIce1ValidateConnectionFrame(logger, null!);
            }
            else
            {
                _sentInitializeFrame(logger, incomingFrameMaxSize, null!);
            }
        }

        internal static void LogSentRequest(this ILogger logger, OutgoingRequestFrame request) =>
            _sentRequestFrame(
                logger,
                request.Path,
                request.Operation,
                request.PayloadSize,
                request.PayloadEncoding,
                request.PayloadCompressionFormat,
                request.Context,
                null!);

        internal static void LogSentResponse(this ILogger logger, OutgoingResponseFrame response) =>
            _sentResponseFrame(
                logger,
                response.ResultType,
                response.PayloadSize,
                response.PayloadEncoding,
                response.PayloadCompressionFormat,
                null!);
    }
}
