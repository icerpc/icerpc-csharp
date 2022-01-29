// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

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
        public static async ValueTask CheckVoidReturnValueAsync(
            this IncomingResponse response,
            IActivator defaultActivator,
            bool hasStream,
            CancellationToken cancel)
        {
            if (response.ResultType == ResultType.Success)
            {
                await response.Payload.ReadVoidAsync(response.GetSlicePayloadEncoding(), hasStream, cancel).ConfigureAwait(false);
            }
            else
            {
                DecodePayloadOptions decodePayloadOptions =
                    response.Features.Get<DecodePayloadOptions>() ?? DecodePayloadOptions.Default;

                throw await response.Payload.ReadRemoteExceptionAsync(
                    response.GetSlicePayloadEncoding(),
                    response.Connection,
                    decodePayloadOptions.ProxyInvoker ?? response.Request.Proxy.Invoker,
                    decodePayloadOptions.Activator ?? defaultActivator,
                    decodePayloadOptions.MaxDepth,
                    cancel).ConfigureAwait(false);
            }
        }

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
                response.Features.Get<DecodePayloadOptions>() ?? DecodePayloadOptions.Default;

            return response.Payload.ToAsyncEnumerable(
                response.GetSlicePayloadEncoding(),
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
        public static async ValueTask<T> ToReturnValueAsync<T>(
            this IncomingResponse response,
            IActivator defaultActivator,
            DecodeFunc<T> decodeFunc,
            bool hasStream,
            CancellationToken cancel)
        {
            DecodePayloadOptions decodePayloadOptions =
                response.Features.Get<DecodePayloadOptions>() ?? DecodePayloadOptions.Default;

            return response.ResultType == ResultType.Success ?
                await response.Payload.ReadValueAsync(
                    response.GetSlicePayloadEncoding(),
                    response.Connection,
                    decodePayloadOptions.ProxyInvoker ?? response.Request.Proxy.Invoker,
                    decodePayloadOptions.Activator ?? defaultActivator,
                    decodePayloadOptions.MaxDepth,
                    decodeFunc,
                    hasStream,
                    cancel).ConfigureAwait(false) :
                throw await response.Payload.ReadRemoteExceptionAsync(
                    response.GetSlicePayloadEncoding(),
                    response.Connection,
                    decodePayloadOptions.ProxyInvoker ?? response.Request.Proxy.Invoker,
                    decodePayloadOptions.Activator ?? defaultActivator,
                    decodePayloadOptions.MaxDepth,
                    cancel).ConfigureAwait(false);
        }
    }
}
