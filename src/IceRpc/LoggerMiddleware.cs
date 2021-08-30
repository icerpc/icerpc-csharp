﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>A middleware that logs requests and responses messages using a logger with "IceRpc" category.
    /// </summary>
    public class LoggerMiddleware : IDispatcher
    {
        private readonly IDispatcher _next;
        private readonly ILogger _logger;

        /// <summary>Constructs a logger middleware.</summary>
        /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
        /// <param name="loggerFactory">The logger factory used to create the logger.</param>
        public LoggerMiddleware(IDispatcher next, ILoggerFactory loggerFactory)
        {
            _next = next;
            _logger = loggerFactory.CreateLogger("IceRpc");
        }

        async ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            _logger.LogReceivedRequest(request.Connection,
                                       request.Path,
                                       request.Operation,
                                       request.PayloadSize,
                                       request.PayloadEncoding);
            try
            {
                OutgoingResponse response = await _next.DispatchAsync(request, cancel).ConfigureAwait(false);
                if (!request.IsOneway)
                {
                    _logger.LogSendingResponse(request.Connection,
                                               request.Path,
                                               request.Operation,
                                               response.ResultType,
                                               response.PayloadSize,
                                               response.PayloadEncoding);
                }
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogDispatchException(request.Connection, request.Path, request.Operation, ex);
                throw;
            }
        }
    }

    internal static partial class LoggerMiddlewareLoggerExtensions
    {
        internal static void LogDispatchException(
            this ILogger logger,
            Connection? connection,
            string path,
            string operation,
            Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogDispatchException(
                    connection?.LocalEndpoint?.ToString() ?? "undefined",
                    connection?.RemoteEndpoint?.ToString() ?? "undefined",
                    path,
                    operation,
                    ex);
            }
        }

        internal static void LogReceivedRequest(
            this ILogger logger,
            Connection? connection,
            string path,
            string operation,
            int payloadSize,
            Encoding payloadEncoding)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogReceivedRequest(
                    connection?.LocalEndpoint?.ToString() ?? "undefined",
                    connection?.RemoteEndpoint?.ToString() ?? "undefined",
                    path,
                    operation,
                    payloadSize,
                    payloadEncoding);
            }
        }

        internal static void LogSendingResponse(
            this ILogger logger,
            Connection? connection,
            string path,
            string operation,
            ResultType resultType,
            int payloadSize,
            Encoding payloadEncoding)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogSendingResponse(
                    connection?.LocalEndpoint?.ToString() ?? "undefined",
                    connection?.RemoteEndpoint?.ToString() ?? "undefined",
                    path,
                    operation,
                    resultType,
                    payloadSize,
                    payloadEncoding);
            }
        }

        [LoggerMessage(
            EventId = (int)LoggerMiddlewareEventIds.DispatchException,
            EventName = nameof(LoggerMiddlewareEventIds.DispatchException),
            Level = LogLevel.Information,
            Message = "request dispatch exception (LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint}, " +
                      "Path={Path}, Operation={Operation})")]
        private static partial void LogDispatchException(
            this ILogger logger,
            string localEndpoint,
            string remoteEndpoint,
            string path,
            string operation,
            Exception ex);

        [LoggerMessage(
            EventId = (int)LoggerMiddlewareEventIds.ReceivedRequest,
            EventName = nameof(LoggerMiddlewareEventIds.ReceivedRequest),
            Level = LogLevel.Information,
            Message = "received request (LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint}, " +
                      "Path={Path}, Operation={Operation}, PayloadSize={PayloadSize}, " +
                      "PayloadEncoding={PayloadEncoding})")]
        private static partial void LogReceivedRequest(
            this ILogger logger,
            string localEndpoint,
            string remoteEndpoint,
            string path,
            string operation,
            int payloadSize,
            Encoding payloadEncoding);

        [LoggerMessage(
            EventId = (int)LoggerMiddlewareEventIds.SendingResponse,
            EventName = nameof(LoggerMiddlewareEventIds.SendingResponse),
            Level = LogLevel.Information,
            Message = "sending response (LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint}, " +
                      "Path={Path}, Operation={Operation}, ResultType={ResultType}, PayloadSize={PayloadSize}, " +
                      "PayloadEncoding={PayloadEncoding})")]
        private static partial void LogSendingResponse(
            this ILogger logger,
            string localEndpoint,
            string remoteEndpoint,
            string path,
            string operation,
            ResultType resultType,
            int payloadSize,
            Encoding payloadEncoding);
    }
}
