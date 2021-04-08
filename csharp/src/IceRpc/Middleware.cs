// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    public static class Middleware
    {
        /// <summary>Creates a middleware that logs request dispatches.</summary>
        public static Func<IDispatcher, IDispatcher> Logger(ILoggerFactory loggerFactory/*, string scope = "" */)
        {
            ILogger logger = loggerFactory.CreateLogger("IceRpc");

            return next => new InlineDispatcher(
                async (current, cancel) =>
                {
                    // TODO: log "'scope' dispatching request ..."
                    try
                    {
                        // TODO: check result and log
                        return await next.DispatchAsync(current, cancel).ConfigureAwait(false);
                    }
                    catch
                    {
                        // TODO: log
                        throw;
                    }
                });
        }
    }
}
