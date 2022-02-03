// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
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
            long errorCode);

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivedStopSendingFrame,
            EventName = nameof(SlicEventIds.ReceivedStopSendingFrame),
            Level = LogLevel.Debug,
            Message = "received Slic StreamStopSending frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogReceivedSlicStopSendingFrame(
            this ILogger logger,
            int frameSize,
            long errorCode);

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivedResumeWrite,
            EventName = nameof(SlicEventIds.ReceivedResumeWrite),
            Level = LogLevel.Debug,
            Message = "received Slic StreamResumeWrite frame (FrameSize={FrameSize}, Size={Size})")]
        internal static partial void LogReceivedSlicResumeWriteFrame(
            this ILogger logger,
            int frameSize,
            int size);

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivedUnidirectionalStreamReleased,
            EventName = nameof(SlicEventIds.ReceivedUnidirectionalStreamReleased),
            Level = LogLevel.Debug,
            Message = "received Slic ReceivedUnidirectionStreamReleased frame")]
        internal static partial void LogReceivedSlicUnidirectionalStreamReleased(this ILogger logger);

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
                    string.Join(", ", body.Parameters.DecodedParameters().Select(pair => $"{pair.Key}={pair.Value}")));
            }
        }

        internal static void LogReceivedSlicInitializeFrame(
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
                    string.Join(", ", body.Parameters.DecodedParameters().Select(pair => $"{pair.Key}={pair.Value}")));
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
            EventId = (int)SlicEventIds.SendFailure,
            EventName = nameof(SlicEventIds.SendFailure),
            Level = LogLevel.Debug,
            Message = "sending of Slic {FrameType} frame failed")]
        internal static partial void LogSendSlicFrameFailure(
            this ILogger logger,
            FrameType frameType,
            Exception exception);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SentResumeWriteFrame,
            EventName = nameof(SlicEventIds.SentResumeWriteFrame),
            Level = LogLevel.Debug,
            Message = "sent Slic StreamResumeWrite frame (FrameSize={FrameSize}, Size={Size})")]
        internal static partial void LogSentSlicResumeWriteFrame(
            this ILogger logger,
            int frameSize,
            int size);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SentStreamFrame,
            EventName = nameof(SlicEventIds.SentStreamFrame),
            Level = LogLevel.Debug,
            Message = "sent Slic {FrameType} frame (FrameSize={FrameSize})")]
        internal static partial void LogSentSlicFrame(this ILogger logger, FrameType frameType, int frameSize);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SentUnidirectionalStreamReleased,
            EventName = nameof(SlicEventIds.SentUnidirectionalStreamReleased),
            Level = LogLevel.Debug,
            Message = "sent Slic UnidirectionalStreamReleased frame")]
        internal static partial void LogSentSlicUnidirectionalStreamReleasedFrame(this ILogger logger);

        internal static void LogSentSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            InitializeAckBody body)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogSentSlicInitializeAckFrame(
                    frameSize,
                    string.Join(", ", body.Parameters.DecodedParameters().Select(pair => $"{pair.Key}={pair.Value}")));
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
                    string.Join(", ", body.Parameters.DecodedParameters().Select(pair => $"{pair.Key}={pair.Value}")));
            }
        }

        [LoggerMessage(
            EventId = (int)SlicEventIds.SentResetFrame,
            EventName = nameof(SlicEventIds.SentResetFrame),
            Level = LogLevel.Debug,
            Message = "sent Slic StreamReset frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogSentSlicResetFrame(
            this ILogger logger,
            int frameSize,
            long errorCode);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SentStopSendingFrame,
            EventName = nameof(SlicEventIds.SentStopSendingFrame),
            Level = LogLevel.Debug,
            Message = "sent Slic StreamStopSending frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogSentSlicStopSendingFrame(
            this ILogger logger,
            int frameSize,
            long errorCode);

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
            Message = "received Slic Initialize frame (FrameSize={FrameSize}, Version={Version}, " +
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
            Message = "received Slic InitializeAck frame (FrameSize={FrameSize}, {Parameters})")]
        private static partial void LogReceivedSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivedVersionFrame,
            EventName = nameof(SlicEventIds.ReceivedVersionFrame),
            Level = LogLevel.Debug,
            Message = "received Slic Version frame (FrameSize={FrameSize}, Versions=[{Versions}])")]
        private static partial void LogReceivedSlicVersionFrame(this ILogger logger, int FrameSize, string versions);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SentInitializeFrame,
            EventName = nameof(SlicEventIds.SentInitializeFrame),
            Level = LogLevel.Debug,
            Message = "sent Slic Initialize frame (FrameSize={FrameSize}, Version={Version}, Apln={Apln}, " +
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
            Message = "sent Slic InitializeAck frame (FrameSize={FrameSize}, {Parameters})")]
        private static partial void LogSentSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SentVersionFrame,
            EventName = nameof(SlicEventIds.SentVersionFrame),
            Level = LogLevel.Debug,
            Message = "sent Slic Version frame (FrameSize={FrameSize}, Versions=[{Versions}])")]
        private static partial void LogSentSlicVersionFrame(this ILogger logger, int frameSize, string versions);
    }
}
