// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace IceRpc
{
    public static class Middleware
    {
        /// <summary>Creates a middleware that publish dispatch metrics using a <see cref="DispatchEventSource"/>.
        /// </summary>
        /// <param name="eventSource">The event source used to publish the metrics events.</param>
        public static Func<IDispatcher, IDispatcher> CreateMetricsPublisher(DispatchEventSource eventSource) =>
            next => new InlineDispatcher(
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

        /// <summary>A middleware that publish dispatch metrics, using the default
        /// <see cref="DispatchEventSource.Log"/> instance.</summary>
        public static Func<IDispatcher, IDispatcher> MetricsPublisher { get; } =
            CreateMetricsPublisher(DispatchEventSource.Log);
    }
}
