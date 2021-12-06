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
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="cancel">The cancellation token.</param>
        public static async ValueTask CheckVoidReturnValueAsync(
            this IncomingResponse response,
            IInvoker? invoker,
            IIceDecoderFactory<IceDecoder> iceDecoderFactory,
            CancellationToken cancel)
        {
            if (response.ResultType == ResultType.Success)
            {
                await response.PayloadReader.ReadVoidAsync(iceDecoderFactory, cancel).ConfigureAwait(false);
            }
            else
            {
                throw await response.PayloadReader.ReadRemoteExceptionAsync(
                    response.Connection,
                    invoker,
                    iceDecoderFactory,
                    cancel).ConfigureAwait(false);
            }
        }

        /// <summary>Creates an async enumerable over the payload reader of an incoming response.</summary>
        /// <param name="response">The response.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="decodeFunc">The function used to decode the streamed member.</param>
        public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            this IncomingResponse response,
            IInvoker? invoker,
            IIceDecoderFactory<IceDecoder> iceDecoderFactory,
            Func<IceDecoder, T> decodeFunc) =>
            response.PayloadReader.ToAsyncEnumerable<T>(
                response.Connection,
                invoker,
                iceDecoderFactory,
                decodeFunc);

        /// <summary>Decodes a response payload.</summary>
        /// <paramtype name="TDecoder">The type of the Ice decoder.</paramtype>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="response">The incoming response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="decodeFunc">The decode function for the return value.</param>
        /// <param name="hasStream">When true, T is or includes a stream return.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The return value.</returns>
        public static async ValueTask<T> ToReturnValueAsync<TDecoder, T>(
            this IncomingResponse response,
            IInvoker? invoker,
            IIceDecoderFactory<TDecoder> iceDecoderFactory,
            DecodeFunc<TDecoder, T> decodeFunc,
            bool hasStream,
            CancellationToken cancel) where TDecoder : IceDecoder =>
            response.ResultType == ResultType.Success ?
                await response.PayloadReader.ReadValueAsync(
                    response.Connection,
                    invoker,
                    iceDecoderFactory,
                    decodeFunc,
                    hasStream,
                    cancel).ConfigureAwait(false) :
                throw await response.PayloadReader.ReadRemoteExceptionAsync(
                    response.Connection,
                    invoker,
                    iceDecoderFactory,
                    cancel).ConfigureAwait(false);
    }
}
