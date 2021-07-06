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
        /// <returns>The logger interceptor.</returns>
        public static Func<IInvoker, IInvoker> Logger(ILoggerFactory loggerFactory)
        {
            ILogger logger = loggerFactory.CreateLogger("IceRpc");
            return next => new InlineInvoker(
                async (request, cancel) =>
                {
                    // TODO we now log the sending of the request before it is actually sent
                    // and it might never be sent
                    using IDisposable? connectionScope = request.Connection?.StartScope();
                    logger.LogSentRequest(request.Path,
                                          request.Operation,
                                          request.PayloadSize,
                                          request.PayloadEncoding);
                    try
                    {
                        IncomingResponse response = await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                        if (!request.IsOneway)
                        {
                            logger.LogReceivedResponse(response.ResultType);
                        }
                        return response;
                    }
                    catch (Exception ex)
                    {
                        logger.LogRequestException(request.Path, request.Operation, ex);
                        throw;
                    }
                });
        }
    }
}
