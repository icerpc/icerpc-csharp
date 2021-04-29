// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

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

        /// <summary>Creates a middleware that emits dispatch metrics, <see cref="DispatchEventSource"/>.</summary>
        /// <param name="eventSource">The event source used to publish the metrics events, when null the default
        /// <see cref="DispatchEventSource.Log"/> is used.</param>
        public static Func<IDispatcher, IDispatcher> DispatchMetrics(DispatchEventSource? eventSource = null)
        {
            eventSource ??= DispatchEventSource.Log;
            return next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    eventSource.RequestStart(request);
                    try
                    {
                        var response = await next.DispatchAsync(request, cancel).ConfigureAwait(false);
                        if (response.ResultType == ResultType.Failure)
                        {
                            eventSource.RequestFailed(request, "IceRpc.RemoteException");
                        }
                        return response;
                    }
                    catch (OperationCanceledException)
                    {
                        eventSource.RequestCanceled(request);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        eventSource.RequestFailed(request, ex);
                        throw;
                    }
                    finally
                    {
                        eventSource.RequestStop(request);
                    }
                });
        }
    }
}
