// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc
{
    /// <summary>Provides extension methods for <see cref="Proxy"/>.</summary>
    public static class ProxyExtensions
    {
        /// <summary>The invoker that a proxy calls when its invoker is null.</summary>
        internal static IInvoker NullInvoker { get; } =
            new InlineInvoker((request, cancel) =>
                request.Connection?.InvokeAsync(request, cancel) ??
                    throw new ArgumentNullException($"{nameof(request.Connection)} is null", nameof(request)));

        /// <summary>Sends a request to a service and returns the response.</summary>
        /// <param name="proxy">A proxy to the target service.</param>
        /// <param name="operation">The name of the operation.</param>
        /// <param name="payloadEncoding">The encoding of the request payload.</param>
        /// <param name="requestPayload">The payload of the request.</param>
        /// <param name="streamParamSender">The stream param sender.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="idempotent">When true, the request is idempotent.</param>
        /// <param name="oneway">When true, the request is sent oneway and an empty response is returned immediately
        /// after sending the request.</param>
        /// <param name="returnStreamParamReceiver">When true, a stream param receiver will be returned.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The response and the optional stream reader.</returns>
        /// <remarks>This method stores the response features into the invocation's response features when invocation is
        /// not null.</remarks>
        public static Task<(IncomingResponse, StreamParamReceiver?)> InvokeAsync(
            this Proxy proxy,
            string operation,
            Encoding payloadEncoding,
            ReadOnlyMemory<ReadOnlyMemory<byte>> requestPayload,
            IStreamParamSender? streamParamSender = null,
            Invocation? invocation = null,
            bool idempotent = false,
            bool oneway = false,
            bool returnStreamParamReceiver = false,
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

                var request = new OutgoingRequest(proxy.Protocol, path: proxy.Path, operation: operation)
                {
                    AltEndpoints = proxy.AltEndpoints,
                    Connection = proxy.Connection,
                    Deadline = deadline,
                    Endpoint = proxy.Endpoint,
                    Features = invocation?.RequestFeatures ?? FeatureCollection.Empty,
                    IsOneway = oneway || (invocation?.IsOneway ?? false),
                    IsIdempotent = idempotent || (invocation?.IsIdempotent ?? false),
                    Proxy = proxy,
                    PayloadEncoding = payloadEncoding,
                    Payload = requestPayload,
                    StreamParamSender = streamParamSender
                };

                // We perform as much work as possible in a non async method to throw exceptions synchronously.
                Task<IncomingResponse> responseTask = (proxy.Invoker ?? NullInvoker).InvokeAsync(request, cancel);
                return ConvertResponseAsync(request, responseTask, timeoutSource, combinedSource);
            }
            catch
            {
                combinedSource?.Dispose();
                timeoutSource?.Dispose();
                throw;

                // If there is no synchronous exception, ConvertResponseAsync disposes these cancellation sources.
            }

            async Task<(IncomingResponse, StreamParamReceiver?)> ConvertResponseAsync(
                OutgoingRequest request,
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

                    // TODO: temporary
                    _ = await response.GetPayloadAsync(cancel).ConfigureAwait(false);

                    StreamParamReceiver? streamParamReceiver = null;
                    if (returnStreamParamReceiver && request.Stream != null)
                    {
                        streamParamReceiver = new StreamParamReceiver(request.Stream);
                    }
                    return (response, streamParamReceiver);
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
