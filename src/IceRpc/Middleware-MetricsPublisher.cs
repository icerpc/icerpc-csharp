// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Diagnostics.Tracing;

namespace IceRpc
{
    public static partial class Middleware
    {
        /// <summary>Creates a middleware that publishes dispatch metrics using a dispatch event source
        /// <see cref="Metrics.CreateDispatchEventSource(string)"/>.</summary>
        /// <param name="eventSource">The dispatch event source used to publish the metrics events.</param>
        public static Func<IDispatcher, IDispatcher> CreateMetricsPublisher(EventSource eventSource)
        {
            DispatchEventSource dispatchEventSource =
                eventSource as DispatchEventSource ?? 
                throw new ArgumentException("event source must be a valid dispatch event source",
                                            nameof(eventSource)); 
            
            return next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    dispatchEventSource.RequestStart(request);
                    try
                    {
                        OutgoingResponse response = await next.DispatchAsync(request, cancel).ConfigureAwait(false);
                        if (response.ResultType == ResultType.Failure)
                        {
                            dispatchEventSource.RequestFailed(request, "IceRpc.RemoteException");
                        }
                        return response;
                    }
                    catch (OperationCanceledException)
                    {
                        dispatchEventSource.RequestCanceled(request);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        dispatchEventSource.RequestFailed(request, ex);
                        throw;
                    }
                    finally
                    {
                        dispatchEventSource.RequestStop(request);
                    }
                });
        }

        /// <summary>A middleware that publishes dispatch metrics, using the default dispatch event source instance
        /// named "IceRpc.Dispatch".</summary>
        public static Func<IDispatcher, IDispatcher> MetricsPublisher { get; } =
            CreateMetricsPublisher(DispatchEventSource.Log);
    }
}
