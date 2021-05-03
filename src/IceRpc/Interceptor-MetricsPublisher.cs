// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Diagnostics.Tracing;

namespace IceRpc
{
    public static partial class Interceptor
    {
        /// <summary>Creates an interceptor that publishes invocation metrics using an invocation event source
        /// <see cref="Metrics.CreateInvocationEventSource(string)"/>.</summary>
        /// <param name="eventSource">The invocation event source used to publish the metrics events.</param>
        public static Func<IInvoker, IInvoker> CreateMetricsPublisher(EventSource eventSource)
        {
            InvocationEventSource invocationEventSource =
                eventSource as InvocationEventSource ?? 
                throw new ArgumentException("event source must be a valid invocation event source",
                                            nameof(eventSource));

            return next => new InlineInvoker(
                async (request, cancel) =>
                {
                    invocationEventSource.RequestStart(request);
                    try
                    {
                        IncomingResponse response = await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                        if (response.ResultType == ResultType.Failure)
                        {
                            invocationEventSource.RequestFailed(request, "IceRpc.RemoteException");
                        }
                        return response;
                    }
                    catch (OperationCanceledException)
                    {
                        invocationEventSource.RequestCanceled(request);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        invocationEventSource.RequestFailed(request, ex);
                        throw;
                    }
                    finally
                    {
                        invocationEventSource.RequestStop(request);
                    }
                });
        }

        /// <summary>A interceptor that publishes invocation metrics, using the default invocation event source
        /// instance named "IceRpc.Invocation".</summary>
        public static Func<IInvoker, IInvoker> MetricsPublisher { get; } =
            CreateMetricsPublisher(InvocationEventSource.Log);
    }
}
