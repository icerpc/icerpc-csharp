// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>A function that decodes the return value from a Slice-encoded response.</summary>
    /// <typeparam name="T">The type of the return value to read.</typeparam>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="encodeOptions">The encode options of the Prx struct that sent the request.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>A value task that contains the return value or a <see cref="RemoteException"/> when the response
    /// carries a failure.</returns>
    public delegate ValueTask<T> ResponseDecodeFunc<T>(
        IncomingResponse response,
        OutgoingRequest request,
        Configure.SliceEncodeOptions? encodeOptions,
        CancellationToken cancel);

    /// <summary>Provides extension methods for generated Prx structs.</summary>
    // TODO: move to PrxExtensions (not done yet for ease of review)
    public static class ProxyExtensions
    {
        private static readonly IDictionary<RequestFieldKey, OutgoingFieldValue> _idempotentFields =
            new Dictionary<RequestFieldKey, OutgoingFieldValue>
            {
                [RequestFieldKey.Idempotent] = default
            }.ToImmutableDictionary();

        /// <summary>Sends a request to a service and decodes the response.</summary>
        /// <param name="prx">A proxy to the remote service.</param>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="payload">The payload of the request. <c>null</c> is equivalent to an empty payload.</param>
        /// <param name="payloadStream">The optional payload stream of the request.</param>
        /// <param name="responseDecodeFunc">The decode function for the response payload. It decodes and throws a
        /// <see cref="RemoteException"/> when the response payload contains a failure.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="idempotent">When <c>true</c>, the request is idempotent.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The operation's return value.</returns>
        /// <exception cref="RemoteException">Thrown if the response carries a failure.</exception>
        /// <remarks>This method stores the response features into the invocation's response features when
        /// invocation is not null.</remarks>
        public static Task<T> InvokeAsync<TPrx, T>(
            this TPrx prx,
            string operation,
            PipeReader? payload,
            PipeReader? payloadStream,
            ResponseDecodeFunc<T> responseDecodeFunc,
            Invocation? invocation,
            bool idempotent = false,
            CancellationToken cancel = default) where TPrx : struct, IPrx
        {
            if (invocation?.IsOneway == true)
            {
                throw new ArgumentException(
                    "cannot send request for an operation with a return value as oneway",
                    nameof(invocation));
            }

            if (payload == null && payloadStream != null)
            {
                throw new ArgumentNullException(
                    nameof(payload),
                    $"when {nameof(payloadStream)} is not null, {nameof(payload)} cannot be null");
            }

            var request = new OutgoingRequest(prx.Proxy)
            {
                Features = invocation?.Features ?? FeatureCollection.Empty,
                Fields = idempotent ?
                    _idempotentFields : ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty,
                Operation = operation,
                Payload = payload ?? EmptyPipeReader.Instance,
                PayloadStream = payloadStream
            };

            IInvoker invoker = prx.Proxy.Invoker;
            if (invocation != null)
            {
                CheckCancellationToken(invocation, cancel);
                ConfigureTimeout(ref invoker, invocation, request);
            }

            try
            {
                // We perform as much work as possible in a non async method to throw exceptions synchronously.
                return ReadResponseAsync(invoker.InvokeAsync(request, cancel), request);
            }
            catch (Exception exception) // synchronous exception throws by InvokeAsync
            {
                request.Complete(exception);
                throw;
            }
            // if the call succeeds, ReadResponseAsync is responsible for completing the request

            async Task<T> ReadResponseAsync(Task<IncomingResponse> responseTask, OutgoingRequest request)
            {
                Exception? exception = null;
                try
                {
                    IncomingResponse response = await responseTask.ConfigureAwait(false);

                    if (invocation != null)
                    {
                        invocation.Features = request.Features;
                    }
                    return await responseDecodeFunc(response, request, prx.EncodeOptions, cancel).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    exception = ex;
                    throw;
                }
                finally
                {
                    request.Complete(exception);
                }
            }
        }

        /// <summary>Sends a request to a service and decodes the "void" response.</summary>
        /// <param name="prx">A proxy for the remote service.</param>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="encoding">The encoding of the request payload.</param>
        /// <param name="payload">The payload of the request. <c>null</c> is equivalent to an empty payload.</param>
        /// <param name="payloadStream">The payload stream of the request.</param>
        /// <param name="defaultActivator">The optional default activator.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="idempotent">When true, the request is idempotent.</param>
        /// <param name="oneway">When true, the request is sent oneway and an empty response is returned immediately
        /// after sending the request.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A task that completes when the void response is returned.</returns>
        /// <exception cref="RemoteException">Thrown if the response carries a failure.</exception>
        /// <remarks>This method stores the response features into the invocation's response features when invocation is
        /// not null.</remarks>
        public static Task InvokeAsync<TPrx>(
            this TPrx prx,
            string operation,
            SliceEncoding encoding,
            PipeReader? payload,
            PipeReader? payloadStream,
            IActivator? defaultActivator,
            Invocation? invocation,
            bool idempotent = false,
            bool oneway = false,
            CancellationToken cancel = default) where TPrx : struct, IPrx
        {
            if (payload == null && payloadStream != null)
            {
                throw new ArgumentNullException(
                    nameof(payload),
                    $"when {nameof(payloadStream)} is not null, {nameof(payload)} cannot be null");
            }

            var request = new OutgoingRequest(prx.Proxy)
            {
                Features = invocation?.Features ?? FeatureCollection.Empty,
                Fields = idempotent ?
                    _idempotentFields : ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty,
                IsOneway = oneway || (invocation?.IsOneway ?? false),
                Operation = operation,
                Payload = payload ?? EmptyPipeReader.Instance,
                PayloadStream = payloadStream
            };

            IInvoker invoker = prx.Proxy.Invoker;
            if (invocation != null)
            {
                CheckCancellationToken(invocation, cancel);
                ConfigureTimeout(ref invoker, invocation, request);
            }

            try
            {
                // We perform as much work as possible in a non async method to throw exceptions synchronously.
                return ReadResponseAsync(invoker.InvokeAsync(request, cancel), request);
            }
            catch (Exception exception) // synchronous exception thrown by InvokeAsync
            {
                request.Complete(exception);
                throw;
            }
            // if the call succeeds, ReadResponseAsync is responsible for completing the request

            async Task ReadResponseAsync(Task<IncomingResponse> responseTask, OutgoingRequest request)
            {
                Exception? exception = null;
                try
                {
                    IncomingResponse response = await responseTask.ConfigureAwait(false);

                    if (invocation != null)
                    {
                        invocation.Features = request.Features;
                    }

                    await response.DecodeVoidReturnValueAsync(
                        request,
                        encoding,
                        defaultActivator,
                        cancel).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    exception = ex;
                    throw;
                }
                finally
                {
                    request.Complete(exception);
                }
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
                    (ref SliceEncoder encoder) => encoder.EncodeVarInt62(deadline));
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
                    $"{nameof(cancel)} must be cancelable when the invocation deadline is set",
                    nameof(cancel));
            }
        }
    }
}
