// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System;

namespace IceRpc.Internal
{
    /// <summary>This class contains constants used for protocol logging event Ids.</summary>
    internal static class ProtocolLoggerExtensions
    {
        private static readonly Action<ILogger, int, Exception> _datagramMaximumSizeExceeded =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                ProtocolEventIds.DatagramMaximumSizeExceeded,
                "maximum datagram size of {Size} exceeded");

        private static readonly Action<ILogger, int, Exception> _datagramSizeExceededIncomingFrameMaxSize =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                ProtocolEventIds.DatagramSizeExceededIncomingFrameMaxSize,
                "frame with {Size} bytes exceeds IncomingFrameMaxSize connection option value");

        private static readonly Action<ILogger, Exception> _datagramConnectionReceiveCloseConnectionFrame =
            LoggerMessage.Define(
                LogLevel.Debug,
                ProtocolEventIds.DatagramConnectionReceiveCloseConnectionFrame,
                "ignoring close connection frame for datagram connection");

        private static readonly Action<ILogger, Exception> _receivedIce1CloseConnectionFrame =
            LoggerMessage.Define(
                LogLevel.Debug,
                ProtocolEventIds.ReceivedIce1CloseConnectionFrame,
                "received close connection frame");

        private static readonly Action<ILogger, int, Exception> _receivedIce1RequestBatchFrame =
            LoggerMessage.Define<int>(
                LogLevel.Information,
                ProtocolEventIds.ReceivedIce1RequestBatchFrame,
                "received batch request (RequestCount={RequestCount})");

        private static readonly Action<ILogger, Exception> _receivedIce1ValidateConnectionFrame =
            LoggerMessage.Define(
                LogLevel.Debug,
                ProtocolEventIds.ReceivedIce1ValidateConnectionFrame,
                "received validate connection frame");

        private static readonly Action<ILogger, long, long, string, Exception> _receivedGoAwayFrame =
            LoggerMessage.Define<long, long, string>(
                LogLevel.Debug,
                ProtocolEventIds.ReceivedGoAwayFrame,
                "received go away frame (LastBidirectionalStreamId={LastBidirectionalStreamId}, " +
                "LastUnidirectionalStreamId={LastUnidirectionalStreamId}, Reason={Reason})");

        private static readonly Action<ILogger, Exception> _receivedGoAwayCanceledFrame =
            LoggerMessage.Define(
                LogLevel.Debug,
                ProtocolEventIds.ReceivedGoAwayCanceledFrame,
                "received go away canceled frame");

        private static readonly Action<ILogger, int, Exception> _receivedInitializeFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            ProtocolEventIds.ReceivedInitializeFrame,
            "received initialize frame (IncomingFrameMaxSize={IncomingFrameMaxSize})");

        private static readonly Action<ILogger, string, string, int, Encoding, Exception> _receivedRequestFrame =
            LoggerMessage.Define<string, string, int, Encoding>(
                LogLevel.Information,
                ProtocolEventIds.ReceivedRequestFrame,
                "received request (Path={Path}, Operation={Operation}, PayloadSize={PayloadSize}, " +
                "PayloadEncoding={PayloadEncoding})");

        private static readonly Action<ILogger, ResultType, Exception> _receivedResponseFrame =
            LoggerMessage.Define<ResultType>(
                LogLevel.Information,
                ProtocolEventIds.ReceivedResponseFrame,
                "received response (ResultType={ResultType})");

        private static readonly Action<ILogger, string, string, Exception> _requestException =
            LoggerMessage.Define<string, string>(
                LogLevel.Information,
                ProtocolEventIds.RequestException,
                "request exception (Path={Path}, Operation={Operation})");

        private static readonly Action<ILogger, string, string, RetryPolicy, int, int, Exception> _retryRequestRetryableException =
            LoggerMessage.Define<string, string, RetryPolicy, int, int>(
                LogLevel.Debug,
                ProtocolEventIds.RetryRequestRetryableException,
                "retrying request because of retryable exception (Path={Path}, Operation={Operation}, " +
                "RetryPolicy={RetryPolicy}, Attempt={Attempt}/{MaxAttempts})");

        private static readonly Action<ILogger, string, string, RetryPolicy, int, int, Exception> _retryRequestConnectionException =
            LoggerMessage.Define<string, string, RetryPolicy, int, int>(
                LogLevel.Debug,
                ProtocolEventIds.RetryRequestConnectionException,
                "retrying request because of connection exception (Path={Path}, Operation={Operation}, " +
                "RetryPolicy={RetryPolicy}, Attempt={Attempt}/{MaxAttempts})");

        private static readonly Action<ILogger, Exception> _sentIce1ValidateConnectionFrame = LoggerMessage.Define(
            LogLevel.Debug,
            ProtocolEventIds.SentIce1ValidateConnectionFrame,
            "sent validate connection frame");

        private static readonly Action<ILogger, string, Exception> _sentIce1CloseConnectionFrame =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                ProtocolEventIds.SentIce1CloseConnectionFrame,
                "sent close connection frame (Reason={Reason})");

        private static readonly Action<ILogger, long, long, string, Exception> _sentGoAwayFrame =
            LoggerMessage.Define<long, long, string>(
                LogLevel.Debug,
                ProtocolEventIds.SentGoAwayFrame,
                "sent go away frame (LastBidirectionalStreamId={LastBidirectionalStreamId}, " +
                "LastUnidirectionalStreamId={LastUnidirectionalStreamId}, Reason={Reason})");

        private static readonly Action<ILogger, Exception> _sentGoAwayCanceledFrame =
            LoggerMessage.Define(
                LogLevel.Debug,
                ProtocolEventIds.SentGoAwayCanceledFrame,
                "sent go away canceled frame");

        private static readonly Action<ILogger, int, Exception> _sentInitializeFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            ProtocolEventIds.SentInitializeFrame,
            "sent initialize frame (IncomingFrameMaxSize={IncomingFrameMaxSize})");

        private static readonly Action<ILogger, string, string, int, Encoding, Exception> _sentRequestFrame =
            LoggerMessage.Define<string, string, int, Encoding>(
                LogLevel.Information,
                ProtocolEventIds.SentRequestFrame,
                "sent request (Path={Path}, Operation={Operation}, PayloadSize={PayloadSize}, " +
                "PayloadEncoding={PayloadEncoding})");

        private static readonly Action<ILogger, ResultType, int, Encoding, Exception> _sentResponseFrame =
            LoggerMessage.Define<ResultType, int, Encoding>(
                LogLevel.Information,
                ProtocolEventIds.SentResponseFrame,
                "sent response (ResultType={ResultType}, Size={Size}, Encoding={Encoding});");

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
            if (socket.Protocol == Protocol.Ice1)
            {
                _receivedIce1CloseConnectionFrame(logger, null!);
            }
            else
            {
                _receivedGoAwayFrame(logger, lastBidirectionalId, lastUnidirectionalId, message, null!);
            }
        }

        internal static void LogReceivedGoAwayCanceledFrame(this ILogger logger) =>
            _receivedGoAwayCanceledFrame(logger, null!);

        internal static void LogReceivedInitializeFrame(this ILogger logger, MultiStreamSocket socket)
        {
            if (socket.Protocol == Protocol.Ice1)
            {
                _receivedIce1ValidateConnectionFrame(logger, null!);
            }
            else
            {
                _receivedInitializeFrame(logger, socket.PeerIncomingFrameMaxSize!.Value, null!);
            }
        }

        internal static void LogReceivedRequest(this ILogger logger, IncomingRequest request) =>
            _receivedRequestFrame(
                logger,
                request.Path,
                request.Operation,
                request.PayloadSize,
                request.PayloadEncoding,
                null!);

        internal static void LogReceivedResponse(this ILogger logger, IncomingResponse response) =>
            _receivedResponseFrame(logger, response.ResultType, null!);

        internal static void LogRetryRequestRetryableException(
            this ILogger logger,
            RetryPolicy retryPolicy,
            int attempt,
            int maxAttempts,
            OutgoingRequest request,
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
            OutgoingRequest request,
            Exception? ex) =>
            _retryRequestConnectionException(
                logger,
                request.Path,
                request.Operation,
                retryPolicy,
                attempt,
                maxAttempts,
                ex!);

        internal static void LogRequestException(this ILogger logger, OutgoingRequest request, Exception ex) =>
            _requestException(logger, request.Path, request.Operation, ex);

        internal static void LogSentGoAwayFrame(
            this ILogger logger,
            MultiStreamSocket socket,
            long lastBidirectionalId,
            long lastUnidirectionalId,
            string message)
        {
            if (socket.Protocol == Protocol.Ice1)
            {
                _sentIce1CloseConnectionFrame(logger, message, null!);
            }
            else
            {
                _sentGoAwayFrame(logger, lastBidirectionalId, lastUnidirectionalId, message, null!);
            }
        }

        internal static void LogSentGoAwayCanceledFrame(this ILogger logger) =>
            _sentGoAwayCanceledFrame(logger, null!);

        internal static void LogSentInitializeFrame(
            this ILogger logger,
            MultiStreamSocket socket,
            int incomingFrameMaxSize)
        {
            if (socket.Protocol == Protocol.Ice1)
            {
                _sentIce1ValidateConnectionFrame(logger, null!);
            }
            else
            {
                _sentInitializeFrame(logger, incomingFrameMaxSize, null!);
            }
        }

        internal static void LogSentRequest(this ILogger logger, OutgoingRequest request) =>
            _sentRequestFrame(
                logger,
                request.Path,
                request.Operation,
                request.PayloadSize,
                request.PayloadEncoding,
                null!);

        internal static void LogSentResponse(this ILogger logger, OutgoingResponse response) =>
            _sentResponseFrame(
                logger,
                response.ResultType,
                response.PayloadSize,
                response.PayloadEncoding,
                null!);
    }
}
