// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
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
            _logger.LogReceivedRequest(request.Path,
                                       request.Operation,
                                       request.PayloadSize,
                                       request.PayloadEncoding);
            OutgoingResponse response = await _next.DispatchAsync(request, cancel).ConfigureAwait(false);
            if (!request.IsOneway)
            {
                _logger.LogSentResponse(response.ResultType, response.PayloadSize, response.PayloadEncoding);
            }
            return response;
        }
    }
}
