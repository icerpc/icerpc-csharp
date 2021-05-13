// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;

namespace IceRpc
{
    public static partial class Middleware
    {
        /// <summary>An interceptor that logs request and response messages using IceRpc logger.
        /// </summary>
        /// <param name="loggerFactory">A logger factory used to create the IceRpc logger.</param>
        /// <returns>The Logger interceptor.</returns>
        public static Func<IDispatcher, IDispatcher> Logger(ILoggerFactory loggerFactory)
        {
            ILogger logger = loggerFactory.CreateLogger("IceRpc");
            return next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    logger.LogReceivedRequest(request);
                    var response = await next.DispatchAsync(request, cancel).ConfigureAwait(false);
                    if (!request.IsOneway)
                    {
                        logger.LogSentResponse(response);
                    }
                    return response;
                });
        }
    }
}
