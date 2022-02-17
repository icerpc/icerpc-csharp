// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Diagnostics;

namespace IceRpc.Slice
{
    /// <summary>Extension methods to decode the payloads of incoming responses when such payloads are encoded with the
    /// Slice encoding.</summary>
    public static class IncomingResponseExtensions
    {
        /// <summary>Decodes a response when the corresponding operation returns void.</summary>
        /// <param name="response">The incoming response.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="hasStream"><c>true</c> if this void value is followed by a stream parameter;
        /// otherwise, <c>false</c>.</param>
        /// <param name="cancel">The cancellation token.</param>
        public static ValueTask CheckVoidReturnValueAsync(
            this IncomingResponse response,
            IActivator defaultActivator,
            bool hasStream,
            CancellationToken cancel) =>
            response.ResultType == ResultType.Success ?
                response.Payload.ReadVoidAsync((SliceEncoding)response.Request.PayloadEncoding, hasStream, cancel) :
                ThrowRemoteExceptionAsync(
                    response,
                    response.Request.Features.Get<DecodePayloadOptions>() ?? DecodePayloadOptions.Default,
                    defaultActivator,
                    cancel);

        /// <summary>Creates an async enumerable over the payload reader of an incoming response.</summary>
        /// <param name="response">The incoming response.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="decodeFunc">The function used to decode the streamed member.</param>
        public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            this IncomingResponse response,
            IActivator defaultActivator,
            DecodeFunc<T> decodeFunc)
        {
            DecodePayloadOptions decodePayloadOptions =
                response.Request.Features.Get<DecodePayloadOptions>() ?? DecodePayloadOptions.Default;

            return response.Payload.ToAsyncEnumerable(
                (SliceEncoding)response.Request.PayloadEncoding,
                response.Connection,
                decodePayloadOptions.ProxyInvoker ?? response.Request.Proxy.Invoker,
                decodePayloadOptions.Activator ?? defaultActivator,
                decodePayloadOptions.MaxDepth,
                decodeFunc,
                decodePayloadOptions.StreamDecoderOptions);
        }

        /// <summary>Decodes a response payload.</summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="response">The incoming response.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="decodeFunc">The decode function for the return value.</param>
        /// <param name="hasStream"><c>true</c> if the value is followed by a stream parameter;
        /// otherwise, <c>false</c>.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The return value.</returns>
        public static ValueTask<T> ToReturnValueAsync<T>(
            this IncomingResponse response,
            IActivator defaultActivator,
            DecodeFunc<T> decodeFunc,
            bool hasStream,
            CancellationToken cancel)
        {
            DecodePayloadOptions decodePayloadOptions =
                response.Request.Features.Get<DecodePayloadOptions>() ?? DecodePayloadOptions.Default;

            return response.ResultType == ResultType.Success ?
                response.Payload.ReadValueAsync(
                    (SliceEncoding)response.Request.PayloadEncoding,
                    response.Connection,
                    decodePayloadOptions.ProxyInvoker ?? response.Request.Proxy.Invoker,
                    decodePayloadOptions.Activator ?? defaultActivator,
                    decodePayloadOptions.MaxDepth,
                    decodeFunc,
                    hasStream,
                    cancel) :
                ThrowRemoteExceptionAsync<T>(
                    response,
                    decodePayloadOptions,
                    defaultActivator,
                    cancel);
        }

        private static async ValueTask ThrowRemoteExceptionAsync(
            this IncomingResponse response,
            DecodePayloadOptions decodePayloadOptions,
            IActivator defaultActivator,
            CancellationToken cancel) =>
            throw await response.ToRemoteExceptionAsync(
                decodePayloadOptions,
                defaultActivator,
                cancel).ConfigureAwait(false);

        private static async ValueTask<T> ThrowRemoteExceptionAsync<T>(
            this IncomingResponse response,
            DecodePayloadOptions decodePayloadOptions,
            IActivator defaultActivator,
            CancellationToken cancel) =>
            throw await response.ToRemoteExceptionAsync(
                decodePayloadOptions,
                defaultActivator,
                cancel).ConfigureAwait(false);

        private static ValueTask<RemoteException> ToRemoteExceptionAsync(
            this IncomingResponse response,
            DecodePayloadOptions decodePayloadOptions,
            IActivator defaultActivator,
            CancellationToken cancel)
        {
            Debug.Assert(response.ResultType != ResultType.Success);

            var resultType = (SliceResultType)response.ResultType;
            if (resultType is SliceResultType.Failure or SliceResultType.ServiceFailure)
            {
                return response.Payload.ReadRemoteExceptionAsync(
                    resultType == SliceResultType.Failure ?
                        response.Protocol.SliceEncoding! : (SliceEncoding)response.Request.PayloadEncoding,
                    response.Connection,
                    decodePayloadOptions.ProxyInvoker ?? response.Request.Proxy.Invoker,
                    decodePayloadOptions.Activator ?? defaultActivator,
                    decodePayloadOptions.MaxDepth,
                    cancel);
            }
            else
            {
                throw new InvalidDataException($"received response with invalid result type value '{resultType}'");
            }
        }
    }
}
