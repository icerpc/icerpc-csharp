// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>A function that decodes the return value from a Slice-encoded response.</summary>
    /// <typeparam name="T">The type of the return value to read.</typeparam>
    /// <param name="response">The incoming response.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>A value task that contains the return value or a <see cref="RemoteException"/> when the response
    /// carries a failure.</returns>
    public delegate ValueTask<T> ResponseDecodeFunc<T>(IncomingResponse response, CancellationToken cancel);

    /// <summary>Provides extension methods for class Proxy.</summary>
    public static class ProxyExtensions
    {
        /// <summary>Computes the Ice encoding to use when encoding a Slice-generated request.</summary>
        public static IceEncoding GetIceEncoding(this Proxy proxy) =>
            proxy.Encoding as IceEncoding ?? proxy.Protocol?.SliceEncoding ??
                throw new NotSupportedException($"unknown protocol {proxy.Protocol}");

        /// <summary>Sends a request to a service and decodes the response.</summary>
        /// <param name="proxy">A proxy for the remote service.</param>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="payloadEncoding">The encoding of the request payload.</param>
        /// <param name="payloadSource">The payload source of the request.</param>
        /// <param name="payloadSourceStream">The optional payload source stream of the request.</param>
        /// <param name="responseDecodeFunc">The decode function for the response payload. It decodes and throws a
        /// <see cref="RemoteException"/> when the response payload contains a failure.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="idempotent">When <c>true</c>, the request is idempotent.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The operation's return value.</returns>
        /// <exception cref="RemoteException">Thrown if the response carries a failure.</exception>
        /// <remarks>This method stores the response features into the invocation's response features when
        /// invocation is not null.</remarks>
        public static Task<T> InvokeAsync<T>(
            this Proxy proxy,
            string operation,
            IceEncoding payloadEncoding,
            PipeReader payloadSource,
            PipeReader? payloadSourceStream,
            ResponseDecodeFunc<T> responseDecodeFunc,
            Invocation? invocation,
            bool idempotent = false,
            CancellationToken cancel = default)
        {
            if (invocation?.IsOneway == true)
            {
                throw new ArgumentException(
                    "cannot send request for an operation with a return value as oneway",
                    nameof(invocation));
            }

            var request = new OutgoingRequest(proxy, operation)
            {
                Deadline = invocation?.Deadline ?? DateTime.MaxValue,
                Features = invocation?.RequestFeatures ?? FeatureCollection.Empty,
                IsIdempotent = idempotent || (invocation?.IsIdempotent ?? false),
                PayloadEncoding = payloadEncoding,
                PayloadSource = payloadSource,
                PayloadSourceStream = payloadSourceStream
            };

            IInvoker invoker = proxy.Invoker;
            if (invocation != null)
            {
                ConfigureTimeout(ref invoker, invocation);
                CheckCancellationToken(invocation, cancel);
            }

            // We perform as much work as possible in a non async method to throw exceptions synchronously.
            return ReadResponseAsync(invoker.InvokeAsync(request, cancel));

            async Task<T> ReadResponseAsync(Task<IncomingResponse> responseTask)
            {
                IncomingResponse response = await responseTask.ConfigureAwait(false);

                if (invocation != null)
                {
                    invocation.ResponseFeatures = response.Features;
                }

                return await responseDecodeFunc(response, cancel).ConfigureAwait(false);
            }
        }

        /// <summary>Sends a request to a service and decodes the "void" response.</summary>
        /// <param name="proxy">A proxy for the remote service.</param>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="payloadEncoding">The encoding of the request payload.</param>
        /// <param name="payloadSource">The payload source of the request.</param>
        /// <param name="payloadSourceStream">The payload source stream of the request.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="idempotent">When true, the request is idempotent.</param>
        /// <param name="oneway">When true, the request is sent oneway and an empty response is returned immediately
        /// after sending the request.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A task that completes when the void response is returned.</returns>
        /// <exception cref="RemoteException">Thrown if the response carries a failure.</exception>
        /// <remarks>This method stores the response features into the invocation's response features when invocation is
        /// not null.</remarks>
        public static Task InvokeAsync(
            this Proxy proxy,
            string operation,
            IceEncoding payloadEncoding,
            PipeReader payloadSource,
            PipeReader? payloadSourceStream,
            IActivator defaultActivator,
            Invocation? invocation,
            bool idempotent = false,
            bool oneway = false,
            CancellationToken cancel = default)
        {
            var request = new OutgoingRequest(proxy, operation)
            {
                Deadline = invocation?.Deadline ?? DateTime.MaxValue,
                Features = invocation?.RequestFeatures ?? FeatureCollection.Empty,
                IsIdempotent = idempotent || (invocation?.IsIdempotent ?? false),
                IsOneway = oneway || (invocation?.IsOneway ?? false),
                PayloadEncoding = payloadEncoding,
                PayloadSource = payloadSource,
                PayloadSourceStream = payloadSourceStream
            };

            IInvoker invoker = proxy.Invoker;
            if (invocation != null)
            {
                ConfigureTimeout(ref invoker, invocation);
                CheckCancellationToken(invocation, cancel);
            }

            // We perform as much work as possible in a non async method to throw exceptions synchronously.
            return ReadResponseAsync(invoker.InvokeAsync(request, cancel));

            async Task ReadResponseAsync(Task<IncomingResponse> responseTask)
            {
                IncomingResponse response = await responseTask.ConfigureAwait(false);

                if (invocation != null)
                {
                    invocation.ResponseFeatures = response.Features;
                }

                await response.CheckVoidReturnValueAsync(
                    defaultActivator,
                    hasStream: false,
                    cancel).ConfigureAwait(false);
            }
        }

        /// <summary>When <paramref name="invocation"/> does not carry a deadline but sets a timeout, adds the
        /// <see cref="TimeoutInterceptor"/> to <paramref name="invoker"/> with the invocation's timeout.
        /// </summary>
        private static void ConfigureTimeout(ref IInvoker invoker, Invocation invocation)
        {
            if (invocation.Deadline == DateTime.MaxValue && invocation.Timeout != Timeout.InfiniteTimeSpan)
            {
                invoker = new TimeoutInterceptor(invoker, invocation.Timeout);
            }
        }

        /// <summary>Verifies that when <paramref name="invocation"/> carries a deadline, <paramref name="cancel"/> is
        /// cancellable.</summary>
        /// <exception name="ArgumentException">Thrown when the invocation carries a deadline but
        /// <paramref name="cancel"/> is not cancellable.</exception>
        private static void CheckCancellationToken(Invocation invocation, CancellationToken cancel)
        {
            if (invocation.Deadline != DateTime.MaxValue && !cancel.CanBeCanceled)
            {
                throw new ArgumentException(
                    $"{nameof(cancel)} must be cancelable when the invocation deadline is set",
                    nameof(cancel));
            }
        }
    }
}
