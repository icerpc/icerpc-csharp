// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    public static class Interceptor
    {
        /// <summary>Creates an interceptor that publishes invocation metrics using an <see cref="InvocationEventSource"/>.
        /// </summary>
        /// <param name="eventSource">The event source used to publish the metrics events.</param>
        public static Func<IInvoker, IInvoker> CreateMetricsPublisher(InvocationEventSource eventSource) =>
            next => new InlineInvoker(
                async (request, cancel) =>
                {
                    eventSource.RequestStart(request);
                    try
                    {
                        var response = await next.InvokeAsync(request, cancel).ConfigureAwait(false);
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

        /// <summary>A interceptor that publish invocation metrics, using the default
        /// <see cref="InvocationEventSource.Log"/> instance.</summary>
        public static Func<IInvoker, IInvoker> MetricsPublisher { get; } =
            CreateMetricsPublisher(InvocationEventSource.Log);
    }
}
