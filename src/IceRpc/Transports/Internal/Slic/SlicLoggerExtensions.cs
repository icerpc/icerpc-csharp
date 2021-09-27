// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Slic;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>This class contains ILogger extension methods for logging Slic transport messages.</summary>
    internal static partial class SlicLoggerExtensions
    {
        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivedResetFrame,
            EventName = nameof(SlicEventIds.ReceivedResetFrame),
            Level = LogLevel.Debug,
            Message = "received Slic StreamReset frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogReceivedSlicResetFrame(
            this ILogger logger,
            int frameSize,
            StreamError errorCode);

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivedStopSendingFrame,
            EventName = nameof(SlicEventIds.ReceivedStopSendingFrame),
            Level = LogLevel.Debug,
            Message = "received Slic StreamStopSending frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogReceivedSlicStopSendingFrame(
            this ILogger logger,
            int frameSize,
            StreamError errorCode);

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivedConsumedFrame,
            EventName = nameof(SlicEventIds.ReceivedConsumedFrame),
            Level = LogLevel.Debug,
            Message = "received Slic StreamConsumed frame (FrameSize={FrameSize}, ConsumedSize={ConsumedSize})")]
        internal static partial void LogReceivedSlicConsumedFrame(
            this ILogger logger,
            int frameSize,
            int consumedSize);

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivedUnsupportedInitializeFrame,
            EventName = nameof(SlicEventIds.ReceivedUnsupportedInitializeFrame),
            Level = LogLevel.Debug,
            Message = "received Slic Initialize frame with unsupported version (FrameSize={FrameSize}, " +
                      "Version={Version})")]
        internal static partial void LogReceivedSlicUnsupportedInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version);

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivingStreamFrame,
            EventName = nameof(SlicEventIds.ReceivingStreamFrame),
            Level = LogLevel.Debug,
            Message = "receiving Slic {FrameType} frame (FrameSize={FrameSize})")]
        internal static partial void LogReceivingSlicDataFrame(
            this ILogger logger,
            FrameType frameType,
            int frameSize);

        internal static void LogReceivedSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            InitializeAckBody body)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogReceivedSlicInitializeAckFrame(
                    frameSize,
                    string.Join(", ", body.Parameters.DecodedParameters().Select((k, v) => $"{k}={v}")));
            }
        }

        internal static void LogReceivingSlicInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version,
            InitializeBody body)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogReceivedSlicInitializeFrame(
                    frameSize,
                    version,
                    body.ApplicationProtocolName,
                    string.Join(", ", body.Parameters.DecodedParameters().Select((k, v) => $"{k}={v}")));
            }
        }

        internal static void LogReceivedSlicVersionFrame(this ILogger logger, int frameSize, VersionBody body)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogReceivedSlicVersionFrame(frameSize, string.Join(", ", body.Versions));
            }
        }

        [LoggerMessage(
            EventId = (int)SlicEventIds.SendingStreamFrame,
            EventName = nameof(SlicEventIds.SendingStreamFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic {FrameType} frame (FrameSize={FrameSize})")]
        internal static partial void LogSendingSlicFrame(this ILogger logger, FrameType frameType, int frameSize);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SentConsumedFrame,
            EventName = nameof(SlicEventIds.SentConsumedFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic StreamConsumed frame (FrameSize={FrameSize}, ConsumedSize={ConsumedSize})")]
        internal static partial void LogSentSlicConsumedFrame(
            this ILogger logger,
            int frameSize,
            int consumedSize);

        internal static void LogSentSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            InitializeAckBody body)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogSentSlicInitializeAckFrame(
                    frameSize,
                    string.Join(", ", body.Parameters.DecodedParameters().Select((k, v) => $"{k}={v}")));
            }
        }

        internal static void LogSentSlicInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version,
            InitializeBody body)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogSentSlicInitializeFrame(
                    frameSize,
                    version,
                    body.ApplicationProtocolName,
                    string.Join(", ", body.Parameters.DecodedParameters().Select((k, v) => $"{k}={v}")));
            }
        }

        [LoggerMessage(
            EventId = (int)SlicEventIds.SentResetFrame,
            EventName = nameof(SlicEventIds.SentResetFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic StreamReset frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogSentSlicResetFrame(
            this ILogger logger,
            int frameSize,
            StreamError errorCode);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SentStopSendingFrame,
            EventName = nameof(SlicEventIds.SentStopSendingFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic StreamStopSending frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogSentSlicStopSendingFrame(
            this ILogger logger,
            int frameSize,
            StreamError errorCode);

        internal static void LogSentSlicVersionFrame(
            this ILogger logger,
            int frameSize,
            VersionBody body)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogSentSlicVersionFrame(frameSize, string.Join(", ", body.Versions));
            }
        }

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivedInitializeFrame,
            EventName = nameof(SlicEventIds.ReceivedInitializeFrame),
            Level = LogLevel.Debug,
            Message = "receiving Slic Initialize frame (FrameSize={FrameSize}, Version={Version}, " +
                      "Apln={Apln}, {Parameters})")]
        private static partial void LogReceivedSlicInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version,
            string apln,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivedInitializeAckFrame,
            EventName = nameof(SlicEventIds.ReceivedInitializeAckFrame),
            Level = LogLevel.Debug,
            Message = "receiving Slic InitializeAck frame (FrameSize={FrameSize}, {Parameters})")]
        private static partial void LogReceivedSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivedVersionFrame,
            EventName = nameof(SlicEventIds.ReceivedVersionFrame),
            Level = LogLevel.Debug,
            Message = "receiving Slic Version frame (FrameSize={FrameSize}, Versions=[{Versions}])")]
        private static partial void LogReceivedSlicVersionFrame(this ILogger logger, int FrameSize, string versions);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SentInitializeFrame,
            EventName = nameof(SlicEventIds.SentInitializeFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic Initialize frame (FrameSize={FrameSize}, Version={Version}, Apln={Apln}, " +
                      "{Parameters})")]
        private static partial void LogSentSlicInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version,
            string apln,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SentInitializeAckFrame,
            EventName = nameof(SlicEventIds.SentInitializeAckFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic InitializeAck frame (FrameSize={FrameSize}, {Parameters})")]
        private static partial void LogSentSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SentVersionFrame,
            EventName = nameof(SlicEventIds.SentVersionFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic Version frame (FrameSize={FrameSize}, Versions=[{Versions}])")]
        private static partial void LogSentSlicVersionFrame(this ILogger logger, int frameSize, string versions);
    }
}
