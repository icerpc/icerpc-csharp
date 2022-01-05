// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;
namespace IceRpc
{
    /// <summary>Provides extension methods for <see cref="Proxy"/>.</summary>
    public static class ProxyExtensions
    {
        /// <summary>Sends a request to a service and returns the response.</summary>
        /// <param name="proxy">A proxy to the target service.</param>
        /// <param name="operation">The name of the operation.</param>
        /// <param name="payloadEncoding">The encoding of the request payload.</param>
        /// <param name="payloadSource">The payload source of the request.</param>
        /// <param name="payloadSourceStream">The optional payload source stream of the request.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="idempotent">When true, the request is idempotent.</param>
        /// <param name="oneway">When true, the request is sent oneway and an empty response is returned immediately
        /// after sending the request.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <remarks>This method stores the response features into the invocation's response features when invocation is
        /// not null.</remarks>
        public static Task<IncomingResponse> InvokeAsync(
            this Proxy proxy,
            string operation,
            Encoding payloadEncoding,
            PipeReader payloadSource,
            PipeReader? payloadSourceStream = null,
            Invocation? invocation = null,
            bool idempotent = false,
            bool oneway = false,
            CancellationToken cancel = default)
        {
            CancellationTokenSource? timeoutSource = null;
            CancellationTokenSource? combinedSource = null;

            try
            {
                DateTime deadline = invocation?.Deadline ?? DateTime.MaxValue;
                if (deadline == DateTime.MaxValue)
                {
                    TimeSpan timeout = invocation?.Timeout ?? Timeout.InfiniteTimeSpan;
                    if (timeout != Timeout.InfiniteTimeSpan)
                    {
                        deadline = DateTime.UtcNow + timeout;

                        timeoutSource = new CancellationTokenSource(timeout);
                        if (cancel.CanBeCanceled)
                        {
                            combinedSource = CancellationTokenSource.CreateLinkedTokenSource(
                                cancel,
                                timeoutSource.Token);
                            cancel = combinedSource.Token;
                        }
                        else
                        {
                            cancel = timeoutSource.Token;
                        }
                    }
                    // else deadline remains MaxValue
                }
                else if (!cancel.CanBeCanceled)
                {
                    throw new ArgumentException(
                        $"{nameof(cancel)} must be cancelable when the invocation deadline is set",
                        nameof(cancel));
                }

                var request = new OutgoingRequest(proxy, operation)
                {
                    Deadline = deadline,
                    Features = invocation?.RequestFeatures ?? FeatureCollection.Empty,
                    IsOneway = oneway || (invocation?.IsOneway ?? false),
                    IsIdempotent = idempotent || (invocation?.IsIdempotent ?? false),
                    PayloadEncoding = payloadEncoding,
                    PayloadSource = payloadSource,
                    PayloadSourceStream = payloadSourceStream
                };

                // We perform as much work as possible in a non async method to throw exceptions synchronously.
                Task<IncomingResponse> responseTask = proxy.Invoker.InvokeAsync(request, cancel);
                return ConvertResponseAsync(responseTask, timeoutSource, combinedSource);
            }
            catch
            {
                combinedSource?.Dispose();
                timeoutSource?.Dispose();
                throw;

                // If there is no synchronous exception, ConvertResponseAsync disposes these cancellation sources.
            }

            async Task<IncomingResponse> ConvertResponseAsync(
                Task<IncomingResponse> responseTask,
                CancellationTokenSource? timeoutSource,
                CancellationTokenSource? combinedSource)
            {
                try
                {
                    IncomingResponse response = await responseTask.ConfigureAwait(false);

                    if (invocation != null)
                    {
                        invocation.ResponseFeatures = response.Features;
                    }

                    return response;
                }
                finally
                {
                    combinedSource?.Dispose();
                    timeoutSource?.Dispose();
                }
            }
        }
    }
}
