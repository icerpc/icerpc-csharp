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
        /// <param name="defaultIceDecoderFactories">The default Ice decoder factories.</param>
        public static void CheckVoidReturnValue(
            this IncomingResponse response,
            IInvoker? invoker,
            DefaultIceDecoderFactories defaultIceDecoderFactories)
        {
            IceDecoder decoder = response.PayloadEncoding.GetIceDecoderFactory(
                response.Features,
                defaultIceDecoderFactories).CreateIceDecoder(response.Payload, response.Connection, invoker);

            if (response.ResultType == ResultType.Failure)
            {
                throw response.ToRemoteException(decoder);
            }
            else
            {
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
            }
        }

        /// <summary>Decodes a response.</summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="response">The incoming response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <param name="defaultIceDecoderFactories">The default Ice decoder factories.</param>
        /// <param name="decodeFunc">The decode function for the return value.</param>
        /// <returns>The return value.</returns>
        public static T ToReturnValue<T>(
            this IncomingResponse response,
            IInvoker? invoker,
            DefaultIceDecoderFactories defaultIceDecoderFactories,
            DecodeFunc<T> decodeFunc)
        {
            IceDecoder decoder = response.PayloadEncoding.GetIceDecoderFactory(
                response.Features,
                defaultIceDecoderFactories).CreateIceDecoder(response.Payload, response.Connection, invoker);

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

        /// <summary>Decodes a remote exception carried by a response. This decoding is special for 1.1-encoded ice1
        /// responses, because the response can carry an ice1 system exception; for other responses, it simply calls
        /// <see cref="IceDecoder.DecodeException"/>.</summary>
        private static RemoteException ToRemoteException(this IncomingResponse response, IceDecoder decoder)
        {
            RemoteException exception =
                response.Protocol == Protocol.Ice1 &&
                response.PayloadEncoding == Encoding.Ice11 &&
                response.Features.Get<ReplyStatus>() is ReplyStatus replyStatus &&
                replyStatus > ReplyStatus.UserException ?
                    decoder.DecodeIce1SystemException(replyStatus) : decoder.DecodeException();

            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return exception;
        }
    }
}
