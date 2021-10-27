﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>This class contains the ILogger extension methods for logging protocol messages.</summary>
    // TODO: split Ice1 and Ice2 protocol logger extensions.
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
            EventId = (int)ProtocolEventIds.ReceivedRequestFrame,
            EventName = nameof(ProtocolEventIds.ReceivedRequestFrame),
            Level = LogLevel.Debug,
            Message = "received request frame (Path={Path}, Operation={Operation}, PayloadSize={PayloadSize}, " +
                      "PayloadEncoding={PayloadEncoding})")]
        internal static partial void LogReceivedRequestFrame(
            this ILogger logger,
            string path,
            string operation,
            int payloadSize,
            Encoding payloadEncoding);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.ReceivedResponseFrame,
            EventName = nameof(ProtocolEventIds.ReceivedResponseFrame),
            Level = LogLevel.Debug,
            Message = "received response frame (Path={Path}, Operation={Operation}, PayloadSize={PayloadSize}, " +
                      "PayloadEncoding={PayloadEncoding}, ResultType={ResultType})")]
        internal static partial void LogReceivedResponseFrame(
            this ILogger logger,
            string path,
            string operation,
            int payloadSize,
            Encoding payloadEncoding,
            ResultType resultType);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.SentRequestFrame,
            EventName = nameof(ProtocolEventIds.SentRequestFrame),
            Level = LogLevel.Debug,
            Message = "sent request frame (Path={Path}, Operation={Operation}, PayloadSize={PayloadSize}, " +
                      "PayloadEncoding={PayloadEncoding})")]
        internal static partial void LogSentRequestFrame(
            this ILogger logger,
            string path,
            string operation,
            int payloadSize,
            Encoding payloadEncoding);

        [LoggerMessage(
            EventId = (int)ProtocolEventIds.SentResponseFrame,
            EventName = nameof(ProtocolEventIds.SentResponseFrame),
            Level = LogLevel.Debug,
            Message = "sent response frame (Path={Path}, Operation={Operation}, PayloadSize={PayloadSize}, " +
                      "PayloadEncoding={PayloadEncoding}, ResultType={ResultType})")]
        internal static partial void LogSentResponseFrame(
            this ILogger logger,
            string path,
            string operation,
            int payloadSize,
            Encoding payloadEncoding,
            ResultType resultType);

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
