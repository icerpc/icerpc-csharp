// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using IceRpc.Slic;

namespace IceRpc
{
    /// <summary>This class contains ILogger extensions methods for logging Slic transport messages.</summary>
    internal static class SlicLoggerExtensions
    {
        private const int BaseEventId = LoggerExtensions.SlicBaseEventId;
        private const int ReceivedFrame = BaseEventId + 0;
        private const int ReceivedInitializeFrame = BaseEventId + 1;
        private const int ReceivedInitializeAckFrame = BaseEventId + 2;
        private const int ReceivedResetFrame = BaseEventId + 3;
        private const int ReceivedVersionFrame = BaseEventId + 4;
        private const int SentFrame = BaseEventId + 5;
        private const int SentInitializeFrame = BaseEventId + 6;
        private const int SentInitializeAckFrame = BaseEventId + 7;
        private const int SentResetFrame = BaseEventId + 8;
        private const int SentVersionFrame = BaseEventId + 9;

        private static readonly Action<ILogger, SlicDefinitions.FrameType, int, Exception> _receivedFrame =
            LoggerMessage.Define<SlicDefinitions.FrameType, int>(
                LogLevel.Debug,
                new EventId(ReceivedFrame, nameof(ReceivedFrame)), "received Slic {FrameType} frame (Size={Size})");

        private static readonly Action<ILogger, int, StreamResetErrorCode, Exception> _receivedResetFrame =
            LoggerMessage.Define<int, StreamResetErrorCode>(
                LogLevel.Debug,
                new EventId(ReceivedResetFrame, nameof(ReceivedResetFrame)),
                "received Slic StreamReset frame (Size={Size}, ResetCode={ResetCode})");

        private static readonly Action<ILogger, SlicDefinitions.FrameType, int, Exception> _sentFrame =
            LoggerMessage.Define<SlicDefinitions.FrameType, int>(
                LogLevel.Debug,
                new EventId(SentFrame, nameof(SentFrame)),
                "sent Slic {FrameType} frame (Size={Size})");

        private static readonly Action<ILogger, int, StreamResetErrorCode, Exception> _sentResetFrame =
            LoggerMessage.Define<int, StreamResetErrorCode>(
                LogLevel.Debug,
                new EventId(SentResetFrame, nameof(SentResetFrame)),
                "sent Slic StreamReset frame (Size={Size}, ErrorCode={ErrorCode})");

        internal static void LogReceivedSlicFrame(this ILogger logger, SlicDefinitions.FrameType type, int frameSize) =>
            _receivedFrame(logger, type, frameSize, null!);

        internal static void LogReceivedSlicInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version,
            InitializeHeaderBody body,
            Dictionary<ParameterKey, ulong> parameters) =>
            LogSlicInitializeFrame(
                logger,
                new EventId(ReceivedInitializeFrame, nameof(ReceivedInitializeFrame)),
                frameSize,
                version,
                body,
                parameters);

        internal static void LogReceivedSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            Dictionary<ParameterKey, ulong> parameters) =>
            LogSlicInitializeAckFrame(
                logger,
                new EventId(ReceivedInitializeAckFrame, nameof(ReceivedInitializeAckFrame)),
                frameSize,
                parameters);

        internal static void LogReceivedSlicResetFrame(
            this ILogger logger,
            int frameSize,
            StreamResetErrorCode errorCode) =>
            _receivedResetFrame(logger, frameSize, errorCode, null!);

        internal static void LogReceivedSlicVersionFrame(
            this ILogger logger,
            int frameSize,
            VersionBody body) =>
            LogSlicVersionFrame(
                logger,
                new EventId(ReceivedVersionFrame, nameof(ReceivedVersionFrame)),
                frameSize,
                body);

        internal static void LogSlicReceivedUnsupportedInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version) =>
            logger.Log(
                LogLevel.Debug,
                SentInitializeAckFrame,
                "received Slic Initialize frame with unsupported version (Size={Size}, Version={Version})",
                frameSize,
                version);

        internal static void LogSentSlicResetFrame(
            this ILogger logger,
            int frameSize,
            StreamResetErrorCode errorCode) =>
            _sentResetFrame(logger, frameSize, errorCode, null!);

        internal static void LogSentSlicFrame(this ILogger logger, SlicDefinitions.FrameType type, int frameSize) =>
            _sentFrame(logger, type, frameSize, null!);

        internal static void LogSentSlicInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version,
            InitializeHeaderBody body,
            Dictionary<ParameterKey, ulong> parameters) =>
            LogSlicInitializeFrame(
                logger,
                new EventId(SentInitializeFrame, nameof(SentInitializeFrame)),
                frameSize,
                version,
                body,
                parameters);

        internal static void LogSentSlicInitializeAckFrame(
            this ILogger logger,
            int frameSize,
            Dictionary<ParameterKey, ulong> parameters) =>
            LogSlicInitializeAckFrame(
                logger,
                new EventId(SentInitializeAckFrame, nameof(SentInitializeAckFrame)),
                frameSize,
                parameters);

        internal static void LogSentSlicVersionFrame(
            this ILogger logger,
            int frameSize,
            VersionBody body) =>
            LogSlicVersionFrame(
                logger,
                new EventId(SentVersionFrame, nameof(SentVersionFrame)),
                frameSize,
                body);

        internal static void LogSlicInitializeFrame(
            this ILogger logger,
            EventId eventId,
            int frameSize,
            uint version,
            InitializeHeaderBody body,
            Dictionary<ParameterKey, ulong> parameters)
        {
            logger.Log(
                LogLevel.Debug,
                eventId,
                eventId.Id == SentInitializeFrame ?
                    "sent Slic Initialize frame (Size={Size}, Version={Version}, Apln={Apln}, {Parameters})" :
                    "received Slic Initialize frame (Size={Size}, Version={Version}, Apln={Apln}, {Parameters})",
                frameSize,
                version,
                body.ApplicationProtocolName,
                string.Join(", ", parameters.Select(pair => $"{pair.Key}={pair.Value}")));
        }

        internal static void LogSlicInitializeAckFrame(
            this ILogger logger,
            EventId eventId,
            int frameSize,
            Dictionary<ParameterKey, ulong> parameters) =>
            logger.Log(
                LogLevel.Debug,
                SentInitializeAckFrame,
                eventId.Id == SentInitializeAckFrame ?
                    "sent Slic InitializeAck frame (Size={Size}, {Parameters})" :
                    "received Slic InitializeAck frame (Size={Size}, {Parameters})",
                frameSize,
                string.Join(", ", parameters.Select(pair => $"{pair.Key}={pair.Value}")));

        internal static void LogSlicVersionFrame(
            this ILogger logger,
            EventId eventId,
            int frameSize,
            VersionBody body)
        {
            logger.Log(
                LogLevel.Debug,
                SentInitializeAckFrame,
                eventId.Id == SentVersionFrame ?
                    "sent Slic Version frame (Size={Size}, Versions=[{Versions}])" :
                    "received Slic Version frame (Size={Size}, Versions=[{Versions}])",
                frameSize,
                string.Join(", ", body.Versions));
        }
    }
}
