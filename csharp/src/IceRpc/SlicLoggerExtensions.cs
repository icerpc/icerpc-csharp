// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace IceRpc
{
    /// <summary>This class contains ILogger extensions methods for logging WebSocket transport messages.</summary>
    internal static class SlicLoggerExtensions
    {
        private const int BaseEventId = LoggerExtensions.SlicBaseEventId;
        private const int ReceivedFrame = BaseEventId + 0;
        private const int ReceivedStreamFrame = BaseEventId + 1;
        private const int SendingFrame = BaseEventId + 2;
        private const int SendingStreamFrame = BaseEventId + 3;

        private static readonly Action<ILogger, string, int, Exception> _receivedFrame =
            LoggerMessage.Define<string, int>(
                LogLevel.Debug,
                new EventId(ReceivedFrame, nameof(ReceivedFrame)),
                "received Slic {FrameType} frame: size = {Size}");

        private static readonly Action<ILogger, string, int, long, Exception> _receivedStreamFrame =
            LoggerMessage.Define<string, int, long>(
                LogLevel.Debug,
                new EventId(ReceivedStreamFrame, nameof(ReceivedStreamFrame)),
                "received Slic {FrameType} frame: size = {Size}, streamId = {StreamId}");

        private static readonly Action<ILogger, string, int, Exception> _sendingFrame =
            LoggerMessage.Define<string, int>(
                LogLevel.Debug,
                new EventId(SendingFrame, nameof(SendingFrame)),
                "sending Slic {FrameType} frame: size = {Size}");

        private static readonly Action<ILogger, string, int, long, Exception> _sendingStreamFrame =
            LoggerMessage.Define<string, int, long>(
                LogLevel.Debug,
                new EventId(SendingStreamFrame, nameof(SendingStreamFrame)),
                "sending Slic {FrameType} frame: size = {Size}, streamId = {StreamId}");

        internal static void LogReceivedSlicFrame(
            this ILogger logger,
            SlicDefinitions.FrameType frameType,
            int frameSize,
            long? streamId)
        {
            if (streamId == null)
            {
                _receivedFrame(logger, frameType.ToString(), frameSize, null!);
            }
            else
            {
                _receivedStreamFrame(logger, frameType.ToString(), frameSize, streamId.Value, null!);
            }
        }

        internal static void LogSendingSlicFrame(
            this ILogger logger,
            SlicDefinitions.FrameType frameType,
            int frameSize,
            long? streamId)
        {
            if (streamId == null)
            {
                _sendingFrame(logger, frameType.ToString(), frameSize, null!);
            }
            else
            {
                _sendingStreamFrame(logger, frameType.ToString(), frameSize, streamId.Value, null!);
            }
        }
    }
}
