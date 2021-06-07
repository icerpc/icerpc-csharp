// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slic;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IceRpc.Internal
{
    /// <summary>This class contains constants used for Slic logging event Ids.</summary>
    internal static class SlicLoggerExtensions
    {
        private static readonly Action<ILogger, SlicDefinitions.FrameType, int, Exception> _receivedFrame =
            LoggerMessage.Define<SlicDefinitions.FrameType, int>(
                LogLevel.Debug,
                SlicEventIds.ReceivedFrame,
                "received Slic {FrameType} frame (Size={Size})");

        private static readonly Action<ILogger, int, SocketStreamErrorCode, Exception> _receivedResetFrame =
            LoggerMessage.Define<int, SocketStreamErrorCode>(
                LogLevel.Debug,
                SlicEventIds.ReceivedResetFrame,
                "received Slic StreamReset frame (Size={Size}, ResetCode={ResetCode})");

        private static readonly Action<ILogger, SlicDefinitions.FrameType, int, Exception> _sentFrame =
            LoggerMessage.Define<SlicDefinitions.FrameType, int>(
                LogLevel.Debug,
                SlicEventIds.SentFrame,
                "sent Slic {FrameType} frame (Size={Size})");

        private static readonly Action<ILogger, int, SocketStreamErrorCode, Exception> _sentResetFrame =
            LoggerMessage.Define<int, SocketStreamErrorCode>(
                LogLevel.Debug,
                SlicEventIds.SentResetFrame,
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
                SlicEventIds.ReceivedInitializeFrame,
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
                SlicEventIds.ReceivedInitializeAckFrame,
                frameSize,
                parameters);

        internal static void LogReceivedSlicResetFrame(
            this ILogger logger,
            int frameSize,
            SocketStreamErrorCode errorCode) =>
            _receivedResetFrame(logger, frameSize, errorCode, null!);

        internal static void LogReceivedSlicVersionFrame(
            this ILogger logger,
            int frameSize,
            VersionBody body) =>
            LogSlicVersionFrame(
                logger,
                SlicEventIds.ReceivedVersionFrame,
                frameSize,
                body);

        internal static void LogSlicReceivedUnsupportedInitializeFrame(
            this ILogger logger,
            int frameSize,
            uint version) =>
            logger.Log(
                LogLevel.Debug,
                SlicEventIds.SentInitializeAckFrame,
                "received Slic Initialize frame with unsupported version (Size={Size}, Version={Version})",
                frameSize,
                version);

        internal static void LogSentSlicResetFrame(
            this ILogger logger,
            int frameSize,
            SocketStreamErrorCode errorCode) =>
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
                SlicEventIds.SentInitializeFrame,
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
                SlicEventIds.SentInitializeAckFrame,
                frameSize,
                parameters);

        internal static void LogSentSlicVersionFrame(
            this ILogger logger,
            int frameSize,
            VersionBody body)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                LogSlicVersionFrame(
                    logger,
                    SlicEventIds.SentVersionFrame,
                    frameSize,
                    body);
            }
        }

        internal static void LogSlicInitializeFrame(
            this ILogger logger,
            EventId eventId,
            int frameSize,
            uint version,
            InitializeHeaderBody body,
            Dictionary<ParameterKey, ulong> parameters)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.Log(
                    LogLevel.Debug,
                    eventId,
                    eventId == SlicEventIds.SentInitializeFrame ?
                        "sent Slic Initialize frame (Size={Size}, Version={Version}, Apln={Apln}, {Parameters})" :
                        "received Slic Initialize frame (Size={Size}, Version={Version}, Apln={Apln}, {Parameters})",
                    frameSize,
                    version,
                    body.ApplicationProtocolName,
                    string.Join(", ", parameters.Select(pair => $"{pair.Key}={pair.Value}")));
            }
        }

        internal static void LogSlicInitializeAckFrame(
            this ILogger logger,
            EventId eventId,
            int frameSize,
            Dictionary<ParameterKey, ulong> parameters)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.Log(
                    LogLevel.Debug,
                    eventId,
                    eventId == SlicEventIds.SentInitializeAckFrame ?
                        "sent Slic InitializeAck frame (Size={Size}, {Parameters})" :
                        "received Slic InitializeAck frame (Size={Size}, {Parameters})",
                    frameSize,
                    string.Join(", ", parameters.Select(pair => $"{pair.Key}={pair.Value}")));
            }
        }

        internal static void LogSlicVersionFrame(
            this ILogger logger,
            EventId eventId,
            int frameSize,
            VersionBody body)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.Log(
                LogLevel.Debug,
                eventId,
                eventId == SlicEventIds.SentVersionFrame ?
                    "sent Slic Version frame (Size={Size}, Versions=[{Versions}])" :
                    "received Slic Version frame (Size={Size}, Versions=[{Versions}])",
                frameSize,
                string.Join(", ", body.Versions));
            }
        }
    }
}
