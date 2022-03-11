// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;
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
        private static readonly IDictionary<RequestFieldKey, OutgoingFieldValue> _idempotentFields =
            new Dictionary<RequestFieldKey, OutgoingFieldValue>
            {
                [RequestFieldKey.Idempotent] = default
            }.ToImmutableDictionary();

        /// <summary>Computes the Slice encoding to use when encoding a Slice-generated request.</summary>
        public static SliceEncoding GetSliceEncoding(this Proxy proxy) =>
            proxy.Encoding as SliceEncoding ?? proxy.Protocol?.SliceEncoding ??
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
            SliceEncoding payloadEncoding,
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

            var request = new OutgoingRequest(proxy)
            {
                Features = invocation?.Features ?? FeatureCollection.Empty,
                Fields = idempotent ?
                    _idempotentFields : ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty,
                Operation = operation,
                PayloadEncoding = payloadEncoding,
                PayloadSource = payloadSource,
                PayloadSourceStream = payloadSourceStream
            };

            IInvoker invoker = proxy.Invoker;
            if (invocation != null)
            {
                CheckCancellationToken(invocation, cancel);
                ConfigureTimeout(ref invoker, invocation, request);
            }

            // We perform as much work as possible in a non async method to throw exceptions synchronously.
            return ReadResponseAsync(invoker.InvokeAsync(request, cancel));

            async Task<T> ReadResponseAsync(Task<IncomingResponse> responseTask)
            {
                IncomingResponse response = await responseTask.ConfigureAwait(false);

                if (invocation != null)
                {
                    invocation.Response = response;
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
            SliceEncoding payloadEncoding,
            PipeReader payloadSource,
            PipeReader? payloadSourceStream,
            IActivator defaultActivator,
            Invocation? invocation,
            bool idempotent = false,
            bool oneway = false,
            CancellationToken cancel = default)
        {
            var request = new OutgoingRequest(proxy)
            {
                Features = invocation?.Features ?? FeatureCollection.Empty,
                Fields = idempotent ?
                    _idempotentFields : ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty,
                IsOneway = oneway || (invocation?.IsOneway ?? false),
                Operation = operation,
                PayloadEncoding = payloadEncoding,
                PayloadSource = payloadSource,
                PayloadSourceStream = payloadSourceStream
            };

            IInvoker invoker = proxy.Invoker;
            if (invocation != null)
            {
                CheckCancellationToken(invocation, cancel);
                ConfigureTimeout(ref invoker, invocation, request);
            }

            // We perform as much work as possible in a non async method to throw exceptions synchronously.
            return ReadResponseAsync(invoker.InvokeAsync(request, cancel));

            async Task ReadResponseAsync(Task<IncomingResponse> responseTask)
            {
                IncomingResponse response = await responseTask.ConfigureAwait(false);

                if (invocation != null)
                {
                    invocation.Response = response;
                }

                await response.CheckVoidReturnValueAsync(
                    defaultActivator,
                    hasStream: false,
                    cancel).ConfigureAwait(false);
            }
        }

        /// <summary>When <paramref name="invocation"/> does not carry a deadline but sets a timeout, adds the
        /// <see cref="TimeoutInterceptor"/> to <paramref name="invoker"/> with the invocation's timeout. Otherwise
        /// if the request carries a deadline add it to the request fields.</summary>
        private static void ConfigureTimeout(ref IInvoker invoker, Invocation invocation, OutgoingRequest request)
        {
            if (invocation.Deadline == DateTime.MaxValue)
            {
                if (invocation.Timeout != Timeout.InfiniteTimeSpan)
                {
                    invoker = new TimeoutInterceptor(invoker, invocation.Timeout);
                }
            }
            else
            {
                long deadline = (long)(invocation.Deadline - DateTime.UnixEpoch).TotalMilliseconds;
                request.Fields = request.Fields.With(
                    RequestFieldKey.Deadline,
                    (ref SliceEncoder encoder) => encoder.EncodeVarLong(deadline));
            }
        }

        /// <summary>Verifies that when <paramref name="invocation"/> carries a deadline, <paramref name="cancel"/> is
        /// cancelable.</summary>
        /// <exception cref="ArgumentException">Thrown when the invocation carries a deadline but
        /// <paramref name="cancel"/> is not cancelable.</exception>
        private static void CheckCancellationToken(Invocation invocation, CancellationToken cancel)
        {
            if (invocation.Deadline != DateTime.MaxValue && !cancel.CanBeCanceled)
            {
                throw new ArgumentException(
                    $"{nameof(cancel)} must be cancellable when the invocation deadline is set",
                    nameof(cancel));
            }
        }
    }
}
