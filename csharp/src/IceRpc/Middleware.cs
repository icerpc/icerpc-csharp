// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A delegate with a simpler signature than a middleware delegate. It can be converted to a regular
    /// middleware with <see cref="Middleware.From"/>.</summary>
    /// <param name="current">The request being dispatched.</param>
    /// <param name="next">A wrapper for the next middleware in the pipeline.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>A value task that holds the outgoing response frame.</returns>
    public delegate ValueTask<OutgoingResponseFrame> SimpleMiddleware(
        Current current,
        Func<ValueTask<OutgoingResponseFrame>> next,
        CancellationToken cancel);

    public static class Middleware
    {
        /// <summary>Creates a middleware that logs request dispatches.</summary>
        public static Func<IDispatcher, IDispatcher> Logger(ILoggerFactory loggerFactory/*, string scope = "" */)
        {
            ILogger logger = loggerFactory.CreateLogger("IceRpc");

            return From(
                async (current, next, cancel) =>
                {
                    // TODO: log "`scope` dispatching request ..."
                    try
                    {
                        // TODO: check result and log
                        return await next().ConfigureAwait(false);
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

            return From(
                (current, next, cancel) =>
                {
                    if (current.Path.Count(c => c == '/') > slashLimit)
                    {
                        // TODO: throw a remote exception, e.g. ImplementationLimitException
                        throw new InvalidDataException($"the request's path `{current.Path}' has too many slashes");
                    }
                    return next();
                });
        }

        /// <summary>Creates a middleware from a simple middleware.</summary>
        public static Func<IDispatcher, IDispatcher> From(SimpleMiddleware simple) =>
            nextDispatcher => IDispatcher.FromInlineDispatcher(
                (current, cancel) => simple(current,
                                            () => nextDispatcher.DispatchAsync(current, cancel),
                                            cancel));
    }
}
