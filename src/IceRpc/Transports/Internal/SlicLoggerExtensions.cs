// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slic;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace IceRpc.Transports.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging Slic transport messages.</summary>
    internal static partial class SlicLoggerExtensions
    {
        [LoggerMessage(
            EventId = (int)SlicEvent.ReceivedFrame,
            EventName = nameof(SlicEvent.ReceivedFrame),
            Level = LogLevel.Debug,
            Message = "received Slic {FrameType} frame (FrameSize={FrameSize})")]
        internal static partial void LogReceivedSlicFrame(this ILogger logger, SlicDefinitions.FrameType frameType, int frameSize);

        internal static void LogReceivedSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            Dictionary<ParameterKey, ulong> parameters)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogReceivedSlicInitializeAckFrame(
                    frameSize,
                    string.Join(", ", parameters.Select(pair => $"{pair.Key}={pair.Value}")));
            }
        }

        internal static void LogReceivedSlicInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version,
            InitializeHeaderBody body,
            Dictionary<ParameterKey, ulong> parameters)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogReceivedSlicInitializeFrame(
                    frameSize,
                    version,
                    body.ApplicationProtocolName,
                    string.Join(", ", parameters.Select(pair => $"{pair.Key}={pair.Value}")));
            }
        }

        [LoggerMessage(
            EventId = (int)SlicEvent.ReceivedResetFrame,
            EventName = nameof(SlicEvent.ReceivedResetFrame),
            Level = LogLevel.Debug,
            Message = "received Slic StreamReset frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogReceivedSlicResetFrame(
            this ILogger logger,
            int frameSize,
            RpcStreamErrorCode errorCode);

        [LoggerMessage(
            EventId = (int)SlicEvent.ReceivedUnsupportedInitializeFrame,
            EventName = nameof(SlicEvent.ReceivedUnsupportedInitializeFrame),
            Level = LogLevel.Debug,
            Message = "received Slic Initialize frame with unsupported version (FrameSize={FrameSize}, " +
                      "Version={Version})")]
        internal static partial void LogSlicReceivedUnsupportedInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version);

        internal static void LogReceivedSlicVersionFrame(this ILogger logger, int frameSize, VersionBody body)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogReceivedSlicVersionFrame(frameSize, string.Join(", ", body.Versions));
            }
        }

        [LoggerMessage(
            EventId = (int)SlicEvent.SentFrame,
            EventName = nameof(SlicEvent.SentFrame),
            Level = LogLevel.Debug,
            Message = "sent Slic {FrameType} frame (FrameSize={FrameSize})")]
        internal static partial void LogSentSlicFrame(this ILogger logger, SlicDefinitions.FrameType frameType, int frameSize);

        internal static void LogSentSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            Dictionary<ParameterKey, ulong> parameters)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogSentSlicInitializeAckFrame(
                    frameSize,
                    string.Join(", ", parameters.Select(pair => $"{pair.Key}={pair.Value}")));
            }
        }

        internal static void LogSentSlicInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version,
            InitializeHeaderBody body,
            Dictionary<ParameterKey, ulong> parameters)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogSentSlicInitializeFrame(
                    frameSize,
                    version,
                    body.ApplicationProtocolName,
                    string.Join(", ", parameters.Select(pair => $"{pair.Key}={pair.Value}")));
            }
        }

        [LoggerMessage(
            EventId = (int)SlicEvent.SentResetFrame,
            EventName = nameof(SlicEvent.SentResetFrame),
            Level = LogLevel.Debug,
            Message = "sent Slic StreamReset frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogSentSlicResetFrame(
            this ILogger logger,
            int frameSize,
            RpcStreamErrorCode errorCode);

        internal static void LogSentSlicVersionFrame(
            this ILogger logger,
            int frameSize,
            VersionBody body)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogSentSlicVersionFrame(
                    frameSize,
                    string.Join(", ", body.Versions));
            }
        }

        [LoggerMessage(
            EventId = (int)SlicEvent.ReceivedInitializeFrame,
            EventName = nameof(SlicEvent.ReceivedInitializeFrame),
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
            EventId = (int)SlicEvent.ReceivedInitializeAckFrame,
            EventName = nameof(SlicEvent.ReceivedInitializeAckFrame),
            Level = LogLevel.Debug,
            Message = "received Slic InitializeAck frame (FrameSize={FrameSize}, {Parameters})")]
        private static partial void LogReceivedSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEvent.ReceivedVersionFrame,
            EventName = nameof(SlicEvent.ReceivedVersionFrame),
            Level = LogLevel.Debug,
            Message = "received Slic Version frame (FrameSize={FrameSize}, Versions=[{Versions}])")]
        private static partial void LogReceivedSlicVersionFrame(this ILogger logger, int FrameSize, string versions);

        [LoggerMessage(
            EventId = (int)SlicEvent.SentInitializeFrame,
            EventName = nameof(SlicEvent.SentInitializeFrame),
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
            EventId = (int)SlicEvent.SentInitializeAckFrame,
            EventName = nameof(SlicEvent.SentInitializeAckFrame),
            Level = LogLevel.Debug,
            Message = "sent Slic InitializeAck frame (FrameSize={FrameSize}, {Parameters})")]
        private static partial void LogSentSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEvent.SentVersionFrame,
            EventName = nameof(SlicEvent.SentVersionFrame),
            Level = LogLevel.Debug,
            Message = "sent Slic Version frame (FrameSize={FrameSize}, Versions=[{Versions}])")]
        private static partial void LogSentSlicVersionFrame(this ILogger logger, int frameSize, string versions);
    }
}
