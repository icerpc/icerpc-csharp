// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;

namespace IceRpc
{
    public static partial class Interceptors
    {
        /// <summary>An interceptor that logs request and response messages using IceRpc logger.
        /// </summary>
        /// <param name="loggerFactory">A logger factory used to create the IceRpc logger.</param>
        /// <returns>The Logger interceptor.</returns>
        public static Func<IInvoker, IInvoker> Logger(ILoggerFactory loggerFactory)
        {
            ILogger logger = loggerFactory.CreateLogger("IceRpc");
            return next => new InlineInvoker(
                async (request, cancel) =>
                {
                    System.Diagnostics.Debug.Assert(false);
                    logger.LogSentRequest(request);
                    try
                    {
                        IncomingResponse response = await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                        if (!request.IsOneway)
                        {
                            logger.LogReceivedResponse(response);
                        }
                        return response;
                    }
                    catch (Exception ex)
                    {
                        using IDisposable? socketScope = request.Connection?.StartScope();
                        logger.LogRequestException(request, ex);
                        throw;
                    }
                });
        }
    }
}
