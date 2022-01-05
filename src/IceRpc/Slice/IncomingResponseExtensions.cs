// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>Extension methods to decode the payloads of incoming responses when such payloads are encoded with the
    /// Ice encoding.</summary>
    public static class IncomingResponseExtensions
    {
        /// <summary>Decodes a response when the corresponding operation returns void.</summary>
        /// <param name="response">The incoming response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="hasStream"><c>true</c> if this void value is followed by a stream parameter;
        /// otherwise, <c>false</c>.</param>
        /// <param name="cancel">The cancellation token.</param>
        public static async ValueTask CheckVoidReturnValueAsync(
            this IncomingResponse response,
            IInvoker? invoker,
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
                throw await response.Payload.ReadRemoteExceptionAsync(
                    response.GetSlicePayloadEncoding(),
                    response.Connection,
                    invoker,
                    response.Features.Get<IActivator>() ?? defaultActivator,
                    response.Features.GetClassGraphMaxDepth(),
                    cancel).ConfigureAwait(false);
            }
        }

        /// <summary>Creates an async enumerable over the payload reader of an incoming response.</summary>
        /// <param name="response">The response.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="decodeFunc">The function used to decode the streamed member.</param>
        public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            this IncomingResponse response,
            IInvoker? invoker,
            IActivator defaultActivator,
            DecodeFunc<T> decodeFunc) =>
            response.Payload.ToAsyncEnumerable<T>(
                response.GetSlicePayloadEncoding(),
                response.Connection,
                invoker,
                response.Features.Get<IActivator>() ?? defaultActivator,
                response.Features.GetClassGraphMaxDepth(),
                decodeFunc,
                response.Features.Get<StreamDecoderOptions>() ?? StreamDecoderOptions.Default);

        /// <summary>Decodes a response payload.</summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="response">The incoming response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="decodeFunc">The decode function for the return value.</param>
        /// <param name="hasStream"><c>true</c> if the value is followed by a stream parameter;
        /// otherwise, <c>false</c>.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The return value.</returns>
        public static async ValueTask<T> ToReturnValueAsync<T>(
            this IncomingResponse response,
            IInvoker? invoker,
            IActivator defaultActivator,
            DecodeFunc<T> decodeFunc,
            bool hasStream,
            CancellationToken cancel) =>
            response.ResultType == ResultType.Success ?
                await response.Payload.ReadValueAsync(
                    response.GetSlicePayloadEncoding(),
                    response.Connection,
                    invoker,
                    response.Features.Get<IActivator>() ?? defaultActivator,
                    response.Features.GetClassGraphMaxDepth(),
                    decodeFunc,
                    hasStream,
                    cancel).ConfigureAwait(false) :
                throw await response.Payload.ReadRemoteExceptionAsync(
                    response.GetSlicePayloadEncoding(),
                    response.Connection,
                    invoker,
                    response.Features.Get<IActivator>() ?? defaultActivator,
                    response.Features.GetClassGraphMaxDepth(),
                    cancel).ConfigureAwait(false);
    }
}
