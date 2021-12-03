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
                await response.Payload.ReadVoidAsync(iceDecoderFactory, cancel).ConfigureAwait(false);
            }
            else
            {
                throw await response.Payload.ReadRemoteExceptionAsync(
                    iceDecoderFactory,
                    response.Connection,
                    invoker,
                    cancel).ConfigureAwait(false);
            }
        }

        /// <summary>Decodes a response.</summary>
        /// <paramtype name="TDecoder">The type of the Ice decoder.</paramtype>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="response">The incoming response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="decodeFunc">The decode function for the return value.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The return value.</returns>
        public static async ValueTask<T> ToReturnValueAsync<TDecoder, T>(
            this IncomingResponse response,
            IInvoker? invoker,
            IIceDecoderFactory<TDecoder> iceDecoderFactory,
            DecodeFunc<TDecoder, T> decodeFunc,
            CancellationToken cancel) where TDecoder : IceDecoder
        {
            if (response.ResultType == ResultType.Success)
            {
                return await response.Payload.ReadValueAsync(
                    iceDecoderFactory,
                    decodeFunc,
                    response.Connection,
                    invoker,
                    cancel).ConfigureAwait(false);
            }
            else
            {
                throw await response.Payload.ReadRemoteExceptionAsync(
                    iceDecoderFactory,
                    response.Connection,
                    invoker,
                    cancel).ConfigureAwait(false);
            }
        }
    }
}
