// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    public static partial class Interceptors
    {
        /// <summary>Creates an interceptor that publishes invocation metrics using an invocation event source
        /// <see cref="InvocationEventSource"/>.</summary>
        /// <param name="invocationEventSource">The invocation event source used to publish the metrics events.</param>
        public static Func<IInvoker, IInvoker> CreateMetricsPublisher(InvocationEventSource invocationEventSource) =>
            next => new InlineInvoker(
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

        /// <summary>A interceptor that publishes invocation metrics, using the default invocation event source
        /// instance <see cref="InvocationEventSource.Log"/> .</summary>
        public static Func<IInvoker, IInvoker> MetricsPublisher { get; } =
            CreateMetricsPublisher(InvocationEventSource.Log);
    }
}
