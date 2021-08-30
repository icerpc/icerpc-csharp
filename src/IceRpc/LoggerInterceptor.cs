// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>An interceptor that logs request and response messages using a logger with "IceRpc" category.
    /// </summary>
    public class LoggerInterceptor : IInvoker
    {
        private readonly ILogger _logger;
        private readonly IInvoker _next;

        /// <summary>Constructs a logger interceptor.</summary>
        /// <param name="next">The next invoker in the invocation pipeline.</param>
        /// <param name="loggerFactory">The logger factory used to create the logger.</param>
        public LoggerInterceptor(IInvoker next, ILoggerFactory loggerFactory)
        {
            _next = next;
            _logger = loggerFactory.CreateLogger("IceRpc");
        }

        async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            _logger.LogSendingRequest(request.Connection,
                                      request.Path,
                                      request.Operation,
                                      request.PayloadSize,
                                      request.PayloadEncoding);
            try
            {
                IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
                if (!request.IsOneway)
                {
                    _logger.LogReceivedResponse(request.Connection,
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
                _logger.LogInvokeException(request.Connection, request.Path, request.Operation, ex);
                throw;
            }
        }
    }

    /// <summary>This class contains the ILogger extension methods for logging logger interceptor messages.</summary>
    internal static partial class LoggerInterceptorLoggerExtensions
    {
        internal static void LogInvokeException(
            this ILogger logger,
            Connection? connection,
            string path,
            string operation,
            Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInvokeException(
                    connection?.LocalEndpoint?.ToString() ?? "undefined",
                    connection?.RemoteEndpoint?.ToString() ?? "undefined",
                    path,
                    operation,
                    ex);
            }
        }

        internal static void LogReceivedResponse(
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
                logger.LogReceivedResponse(
                    connection?.LocalEndpoint?.ToString() ?? "undefined",
                    connection?.RemoteEndpoint?.ToString() ?? "undefined",
                    path,
                    operation,
                    resultType,
                    payloadSize,
                    payloadEncoding);
            }
        }

        internal static void LogSendingRequest(
            this ILogger logger,
            Connection? connection,
            string path,
            string operation,
            int payloadSize,
            Encoding payloadEncoding)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogSendingRequest(
                    connection?.LocalEndpoint?.ToString() ?? "undefined",
                    connection?.RemoteEndpoint?.ToString() ?? "undefined",
                    path,
                    operation,
                    payloadSize,
                    payloadEncoding);
            }
        }

        [LoggerMessage(
            EventId = (int)LoggerInterceptorEventIds.InvokeException,
            EventName = nameof(LoggerInterceptorEventIds.InvokeException),
            Level = LogLevel.Information,
            Message = "request invocation exception (LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint}, " +
                      "Path={Path}, Operation={Operation})")]
        private static partial void LogInvokeException(
            this ILogger logger,
            string localEndpoint,
            string remoteEndpoint,
            string path,
            string operation,
            Exception ex);

        [LoggerMessage(
            EventId = (int)LoggerInterceptorEventIds.ReceivedResponse,
            EventName = nameof(LoggerInterceptorEventIds.ReceivedResponse),
            Level = LogLevel.Information,
            Message = "received response (LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint}, " +
                      "Path={Path}, Operation={Operation}, ResultType={ResultType}, PayloadSize={PayloadSize}, " +
                      "PayloadEncoding={PayloadEncoding})")]
        private static partial void LogReceivedResponse(
            this ILogger logger,
            string localEndpoint,
            string remoteEndpoint,
            string path,
            string operation,
            ResultType resultType,
            int payloadSize,
            Encoding payloadEncoding);

        [LoggerMessage(
            EventId = (int)LoggerInterceptorEventIds.SendingRequest,
            EventName = nameof(LoggerInterceptorEventIds.SendingRequest),
            Level = LogLevel.Information,
            Message = "sending request (LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint}, " +
                      "Path={Path}, Operation={Operation}, PayloadSize={PayloadSize}, " +
                      "PayloadEncoding={PayloadEncoding})")]
        private static partial void LogSendingRequest(
            this ILogger logger,
            string localEndpoint,
            string remoteEndpoint,
            string path,
            string operation,
            int payloadSize,
            Encoding payloadEncoding);
    }
}
