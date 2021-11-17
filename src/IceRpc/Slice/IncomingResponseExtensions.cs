// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

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
        public static void CheckVoidReturnValue(
            this IncomingResponse response,
            IInvoker? invoker,
            IIceDecoderFactory<IceDecoder> iceDecoderFactory)
        {
            IceDecoder decoder = iceDecoderFactory.CreateIceDecoder(response.Payload, response.Connection, invoker);

            if (response.ResultType == ResultType.Failure)
            {
                throw response.ToRemoteException(decoder);
            }
            else
            {
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
            }
        }

        /// <summary>Decodes a response; only a specific Ice encoding is expected/supported.</summary>
        /// <paramtype name="TDecoder">The type of the Ice decoder.</paramtype>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="response">The incoming response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="decodeFunc">The decode function for the return value.</param>
        /// <returns>The return value.</returns>
        public static T ToReturnValue<TDecoder, T>(
            this IncomingResponse response,
            IInvoker? invoker,
            IIceDecoderFactory<TDecoder> iceDecoderFactory,
            DecodeFunc<TDecoder, T> decodeFunc) where TDecoder : IceDecoder
        {
            if (response.PayloadEncoding != iceDecoderFactory.Encoding)
            {
                throw new InvalidDataException(@$"cannot decode response payload encoded with {response.PayloadEncoding
                    }; expected a payload encoded with {iceDecoderFactory.Encoding}");
            }

            TDecoder decoder = iceDecoderFactory.CreateIceDecoder(response.Payload, response.Connection, invoker);

            if (response.ResultType == ResultType.Failure)
            {
                throw response.ToRemoteException(decoder);
            }
            else
            {
                T result = decodeFunc(decoder);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
                return result;
            }
        }

        /// <summary>Decodes a remote exception carried by a response.</summary>
        private static RemoteException ToRemoteException(this IncomingResponse response, IceDecoder decoder)
        {
            RemoteException exception = decoder is Ice11Decoder decoder11 &&
                response.Features.Get<ReplyStatus>() is ReplyStatus replyStatus &&
                replyStatus > ReplyStatus.UserException ?
                    decoder11.DecodeIce1SystemException(replyStatus) : decoder.DecodeException();

            if (exception is not UnknownSlicedRemoteException)
            {
                decoder.CheckEndOfBuffer(skipTaggedParams: false);
            }
            // else, we did not decode the full exception from the buffer

            return exception;
        }
    }
}
