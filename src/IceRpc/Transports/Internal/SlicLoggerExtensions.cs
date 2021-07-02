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
            EventId = (int)SlicEvent.ReceivedResetFrame,
            EventName = nameof(SlicEvent.ReceivedResetFrame),
            Level = LogLevel.Debug,
            Message = "received Slic StreamReset frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogReceivedSlicResetFrame(
            this ILogger logger,
            int frameSize,
            RpcStreamError errorCode);

        [LoggerMessage(
            EventId = (int)SlicEvent.ReceivedStopSendingFrame,
            EventName = nameof(SlicEvent.ReceivedStopSendingFrame),
            Level = LogLevel.Debug,
            Message = "received Slic StreamStopSending frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogReceivedSlicStopSendingFrame(
            this ILogger logger,
            int frameSize,
            RpcStreamError errorCode);

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

        [LoggerMessage(
            EventId = (int)SlicEvent.ReceivingFrame,
            EventName = nameof(SlicEvent.ReceivingFrame),
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
            EventId = (int)SlicEvent.SendingFrame,
            EventName = nameof(SlicEvent.SendingFrame),
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
            EventId = (int)SlicEvent.SendingResetFrame,
            EventName = nameof(SlicEvent.SendingResetFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic StreamReset frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogSendingSlicResetFrame(
            this ILogger logger,
            int frameSize,
            RpcStreamError errorCode);

        [LoggerMessage(
            EventId = (int)SlicEvent.SendingStopSendingFrame,
            EventName = nameof(SlicEvent.SendingStopSendingFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic StreamStopSending frame (FrameSize={FrameSize}, ErrorCode={ErrorCode})")]
        internal static partial void LogSendingSlicStopSendingFrame(
            this ILogger logger,
            int frameSize,
            RpcStreamError errorCode);

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
            EventId = (int)SlicEvent.ReceivingInitializeFrame,
            EventName = nameof(SlicEvent.ReceivingInitializeFrame),
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
            EventId = (int)SlicEvent.ReceivingInitializeAckFrame,
            EventName = nameof(SlicEvent.ReceivingInitializeAckFrame),
            Level = LogLevel.Debug,
            Message = "receiving Slic InitializeAck frame (FrameSize={FrameSize}, {Parameters})")]
        private static partial void LogReceivingSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEvent.ReceivingVersionFrame,
            EventName = nameof(SlicEvent.ReceivingVersionFrame),
            Level = LogLevel.Debug,
            Message = "receiving Slic Version frame (FrameSize={FrameSize}, Versions=[{Versions}])")]
        private static partial void LogReceivingSlicVersionFrame(this ILogger logger, int FrameSize, string versions);

        [LoggerMessage(
            EventId = (int)SlicEvent.SendingInitializeFrame,
            EventName = nameof(SlicEvent.SendingInitializeFrame),
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
            EventId = (int)SlicEvent.SendingInitializeAckFrame,
            EventName = nameof(SlicEvent.SendingInitializeAckFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic InitializeAck frame (FrameSize={FrameSize}, {Parameters})")]
        private static partial void LogSendingSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            string parameters);

        [LoggerMessage(
            EventId = (int)SlicEvent.SendingVersionFrame,
            EventName = nameof(SlicEvent.SendingVersionFrame),
            Level = LogLevel.Debug,
            Message = "sending Slic Version frame (FrameSize={FrameSize}, Versions=[{Versions}])")]
        private static partial void LogSendingSlicVersionFrame(this ILogger logger, int frameSize, string versions);
    }
}
