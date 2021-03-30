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
                    // TODO: log "`scope` dispatching request ..."
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

        /// <summary>Creates a middleware that limits the number of slashes in a request's path.</summary>
        public static Func<IDispatcher, IDispatcher> SlashLimiter(int slashLimit = 3)
        {
            if (slashLimit < 1)
            {
                throw new ArgumentException($"{nameof(slashLimit)} must be at least 1", nameof(slashLimit));
            }

            return next => new InlineDispatcher(
                (current, cancel) =>
                {
                    if (current.Path.Count(c => c == '/') > slashLimit)
                    {
                        // TODO: throw a remote exception, e.g. ImplementationLimitException
                        throw new InvalidDataException($"the request's path `{current.Path}' has too many slashes");
                    }
                    return next.DispatchAsync(current, cancel);
                });
        }
    }
}
