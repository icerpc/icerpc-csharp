// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.IO.Pipelines;

namespace IceRpc.Tests
{
    public class LogPipeWriterDecorator : PipeWriter
    {
        public override bool CanGetUnflushedBytes => _decoratee.CanGetUnflushedBytes;
        public override long UnflushedBytes => _decoratee.UnflushedBytes;

        private readonly PipeWriter _decoratee;
        private readonly ILogger _logger;

        public LogPipeWriterDecorator(PipeWriter decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }

        public override void Advance(int bytes)
        {
            _decoratee.Advance(bytes);
            _logger.LogAdvance(bytes);
        }

        public override Stream AsStream(bool leaveOpen = false)
        {
            var result = _decoratee.AsStream(leaveOpen);
            _logger.LogAsStream(leaveOpen);
            return result;
        }

        public override void CancelPendingFlush()
        {
            _decoratee.CancelPendingFlush();
            _logger.LogCancelPendingFlush();
        }

        public override void Complete(Exception? exception)
        {
            _logger.LogCompleteBegin(exception);
            _decoratee.Complete(exception);
            _logger.LogCompleteEnd(exception);
        }

        public override async ValueTask CompleteAsync(Exception? exception = default)
        {
            _logger.LogCompleteAsyncBegin(exception);
            await _decoratee.CompleteAsync(exception).ConfigureAwait(false);
            _logger.LogCompleteAsyncEnd(exception);
        }

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken)
        {
            FlushResult flushResult = await _decoratee.FlushAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogFlushAsync(flushResult.IsCompleted, cancellationToken.CanBeCanceled);
            return flushResult;
        }

        public override Memory<byte> GetMemory(int sizeHint)
        {
            Memory<byte> result = _decoratee.GetMemory(sizeHint);
            _logger.LogGetMemory(sizeHint);
            return result;
        }

        public override Span<byte> GetSpan(int sizeHint)
        {
            Span<byte> result = _decoratee.GetSpan(sizeHint);
            _logger.LogGetSpan(sizeHint);
            return result;
        }

        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken)
        {
            FlushResult flushResult = await _decoratee.WriteAsync(source, cancellationToken).ConfigureAwait(false);
            _logger.LogWriteAsync(source.Length, flushResult.IsCompleted, cancellationToken.CanBeCanceled);

            return flushResult;
        }

        // We use the default implementation for CopyFromAsync: it's protected as a result we can't forward
        // it to _decoratee.
    }

    internal static partial class PipeWriterLoggerExtensions
    {
        [LoggerMessage(
            EventId = 1,
            EventName = "Advance",
            Level = LogLevel.Information,
            Message = "Advance (bytes={Bytes})")]
        internal static partial void LogAdvance(this ILogger logger, int bytes);

        [LoggerMessage(
            EventId = 2,
            EventName = "AsStream",
            Level = LogLevel.Information,
            Message = "AsStream (leaveOpen={LeaveOpen})")]
        internal static partial void LogAsStream(this ILogger logger, bool leaveOpen);

        [LoggerMessage(
            EventId = 3,
            EventName = "CancelPendingFlush",
            Level = LogLevel.Information,
            Message = "CancelPendingFlush")]
        internal static partial void LogCancelPendingFlush(this ILogger logger);

        [LoggerMessage(
            EventId = 4,
            EventName = "CompleteBegin",
            Level = LogLevel.Information,
            Message = "CompleteBegin")]
        internal static partial void LogCompleteBegin(this ILogger logger, Exception? exception);

        [LoggerMessage(
            EventId = 5,
            EventName = "CompleteEnd",
            Level = LogLevel.Information,
            Message = "CompleteEnd")]
        internal static partial void LogCompleteEnd(this ILogger logger, Exception? exception);

        [LoggerMessage(
            EventId = 6,
            EventName = "CompleteAsyncBegin",
            Level = LogLevel.Information,
            Message = "CompleteAsyncBegin")]
        internal static partial void LogCompleteAsyncBegin(this ILogger logger, Exception? exception);

        [LoggerMessage(
            EventId = 7,
            EventName = "CompleteAsyncEnd",
            Level = LogLevel.Information,
            Message = "CompleteAsyncEnd")]
        internal static partial void LogCompleteAsyncEnd(this ILogger logger, Exception? exception);

        [LoggerMessage(
            EventId = 8,
            EventName = "FlushAsync",
            Level = LogLevel.Information,
            Message = "FlushAsync (isCompleted={IsCompleted}, canBeCanceled={CanBeCanceled})")]
        internal static partial void LogFlushAsync(this ILogger logger, bool isCompleted, bool canBeCanceled);

        [LoggerMessage(
            EventId = 9,
            EventName = "GetMemory",
            Level = LogLevel.Information,
            Message = "GetMemory (sizeHint={sizeHint})")]
        internal static partial void LogGetMemory(this ILogger logger, int sizeHint);

        [LoggerMessage(
            EventId = 10,
            EventName = "GetSpan",
            Level = LogLevel.Information,
            Message = "GetSpan (sizeHint={sizeHint})")]
        internal static partial void LogGetSpan(this ILogger logger, int sizeHint);

        [LoggerMessage(
            EventId = 11,
            EventName = "WriteAsync",
            Level = LogLevel.Information,
            Message = "WriteAsync (sourceLength={SourceLength}, isCompleted={IsCompleted}, canBeCanceled={CanBeCanceled})")]
        internal static partial void LogWriteAsync(
            this ILogger logger,
            int sourceLength,
            bool isCompleted,
            bool canBeCanceled);
    }
}
