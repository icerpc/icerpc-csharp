// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;

namespace IceRpc
{
    public static partial class Interceptors
    {
        public static Func<IInvoker, IInvoker> Logger(LoggerFactory loggerFactory)
        {
            ILogger logger = loggerFactory.CreateLogger("IceRpc");
            return next => new InlineInvoker(
                async (request, cancel) =>
                {
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
