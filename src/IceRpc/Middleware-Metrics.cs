// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    public static partial class Middleware
    {
        /// <summary>A middleware that publishes dispatch metrics, using the default dispatch event source instance
        /// named "IceRpc.Dispatch".</summary>
        public static Func<IDispatcher, IDispatcher> Metrics { get; } =
            CustomMetrics(DispatchEventSource.Log);

        /// <summary>Creates a middleware that publishes dispatch metrics using a dispatch event source
        /// <see cref="DispatchEventSource"/>.</summary>
        /// <param name="dispatchEventSource">The dispatch event source used to publish the metrics events.</param>
        public static Func<IDispatcher, IDispatcher> CustomMetrics(DispatchEventSource dispatchEventSource) =>
            next => new InlineDispatcher(
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
}
