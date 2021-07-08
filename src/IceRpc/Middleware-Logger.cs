// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;

namespace IceRpc
{
    public static partial class Middleware
    {
        /// <summary>Returns a middleware that logs requests and responses.</summary>
        /// <param name="loggerFactory">A logger factory used to create the IceRpc logger.</param>
        /// <returns>A Logger middleware.</returns>
        public static Func<IDispatcher, IDispatcher> Logger(ILoggerFactory loggerFactory)
        {
            ILogger logger = loggerFactory.CreateLogger("IceRpc");
            return next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    logger.LogReceivedRequest(request.Path,
                                              request.Operation,
                                              request.PayloadSize,
                                              request.PayloadEncoding);
                    OutgoingResponse response = await next.DispatchAsync(request, cancel).ConfigureAwait(false);
                    if (!request.IsOneway)
                    {
                        logger.LogSentResponse(response.ResultType, response.PayloadSize, response.PayloadEncoding);
                    }
                    return response;
                });
        }
    }
}
