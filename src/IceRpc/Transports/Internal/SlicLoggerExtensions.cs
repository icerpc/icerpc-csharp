// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Slic;
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
            EventId = (int)SlicEventIds.ReceivedUnsupportedInitializeFrame,
            EventName = nameof(SlicEventIds.ReceivedUnsupportedInitializeFrame),
            Level = LogLevel.Debug,
            Message = "received Slic Initialize frame with unsupported version (FrameSize={FrameSize}, " +
                      "Version={Version})")]
        internal static partial void LogSlicReceivedUnsupportedInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version);

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivingFrame,
            EventName = nameof(SlicEventIds.ReceivingFrame),
            Level = LogLevel.Debug,
            Message = "receiving Slic {FrameType} frame (FrameSize={FrameSize})")]
        internal static partial void LogReceivingSlicFrame(
            this ILogger logger,
            SlicDefinitions.FrameType frameType,
            int frameSize);

        internal static void LogReceivingSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            Dictionary<ParameterKey, ulong> parameters)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogReceivingSlicInitializeAckFrame(
                    frameSize,
                    string.Join(", ", parameters.Select(pair => $"{pair.Key}={pair.Value}")));
            }
        }

        internal static void LogReceivingSlicInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version,
            InitializeHeaderBody body,
            Dictionary<ParameterKey, ulong> parameters)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogReceivingSlicInitializeFrame(
                    frameSize,
                    version,
                    body.ApplicationProtocolName,
                    string.Join(", ", parameters.Select(pair => $"{pair.Key}={pair.Value}")));
            }
        }

        internal static void LogReceivingSlicVersionFrame(this ILogger logger, int frameSize, VersionBody body)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogReceivingSlicVersionFrame(frameSize, string.Join(", ", body.Versions));
            }
        }

        [LoggerMessage(
            EventId = (int)SlicEventIds.SendingFrame,
            EventName = nameof(SlicEventIds.SendingFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic {FrameType} frame (FrameSize={FrameSize})")]
        internal static partial void LogSendingSlicFrame(this ILogger logger, SlicDefinitions.FrameType frameType, int frameSize);

        internal static void LogSendingSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            Dictionary<ParameterKey, ulong> parameters)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogSendingSlicInitializeAckFrame(
                    frameSize,
                    string.Join(", ", parameters.Select(pair => $"{pair.Key}={pair.Value}")));
            }
        }

        internal static void LogSendingSlicInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version,
            InitializeHeaderBody body,
            Dictionary<ParameterKey, ulong> parameters)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogSendingSlicInitializeFrame(
                    frameSize,
                    version,
                    body.ApplicationProtocolName,
                    string.Join(", ", parameters.Select(pair => $"{pair.Key}={pair.Value}")));
            }
        }

        [LoggerMessage(
            EventId = (int)SlicEventIds.SendingResetFrame,
            EventName = nameof(SlicEventIds.SendingResetFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic StreamReset frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogSendingSlicResetFrame(
            this ILogger logger,
            int frameSize,
            StreamError errorCode);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SendingStopSendingFrame,
            EventName = nameof(SlicEventIds.SendingStopSendingFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic StreamStopSending frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogSendingSlicStopSendingFrame(
            this ILogger logger,
            int frameSize,
            StreamError errorCode);

        internal static void LogSendingSlicVersionFrame(
            this ILogger logger,
            int frameSize,
            VersionBody body)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogSendingSlicVersionFrame(
                    frameSize,
                    string.Join(", ", body.Versions));
            }
        }

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivingInitializeFrame,
            EventName = nameof(SlicEventIds.ReceivingInitializeFrame),
            Level = LogLevel.Debug,
            Message = "receiving Slic Initialize frame (FrameSize={FrameSize}, Version={Version}, " +
                      "Apln={Apln}, {Parameters})")]
        private static partial void LogReceivingSlicInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version,
            string apln,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivingInitializeAckFrame,
            EventName = nameof(SlicEventIds.ReceivingInitializeAckFrame),
            Level = LogLevel.Debug,
            Message = "receiving Slic InitializeAck frame (FrameSize={FrameSize}, {Parameters})")]
        private static partial void LogReceivingSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEventIds.ReceivingVersionFrame,
            EventName = nameof(SlicEventIds.ReceivingVersionFrame),
            Level = LogLevel.Debug,
            Message = "receiving Slic Version frame (FrameSize={FrameSize}, Versions=[{Versions}])")]
        private static partial void LogReceivingSlicVersionFrame(this ILogger logger, int FrameSize, string versions);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SendingInitializeFrame,
            EventName = nameof(SlicEventIds.SendingInitializeFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic Initialize frame (FrameSize={FrameSize}, Version={Version}, Apln={Apln}, " +
                      "{Parameters})")]
        private static partial void LogSendingSlicInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version,
            string apln,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SendingInitializeAckFrame,
            EventName = nameof(SlicEventIds.SendingInitializeAckFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic InitializeAck frame (FrameSize={FrameSize}, {Parameters})")]
        private static partial void LogSendingSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEventIds.SendingVersionFrame,
            EventName = nameof(SlicEventIds.SendingVersionFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic Version frame (FrameSize={FrameSize}, Versions=[{Versions}])")]
        private static partial void LogSendingSlicVersionFrame(this ILogger logger, int frameSize, string versions);
    }
}
