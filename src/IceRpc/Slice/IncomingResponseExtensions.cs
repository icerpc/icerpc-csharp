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
        /// <param name="_">The cancellation token.</param>
        public static ValueTask CheckVoidReturnValueAsync(
            this IncomingResponse response,
            IInvoker? invoker,
            IIceDecoderFactory<IceDecoder> iceDecoderFactory,
            CancellationToken _)
        {
            if (response.Payload.Length > 0)
            {
                IceDecoder decoder = iceDecoderFactory.CreateIceDecoder(response.Payload, response.Connection, invoker);

                decoder.DecodeFixedLengthSize(); // skip return size for now

                if (response.ResultType == ResultType.Failure)
                {
                    throw response.ToRemoteException(decoder);
                }
                else
                {
                    decoder.CheckEndOfBuffer(skipTaggedParams: true);
                }
            }
            // else check is successful. Payload has length 0 for oneway responses.

            return default;
        }

        /// <summary>Decodes a response; only a specific Ice encoding is expected/supported.</summary>
        /// <paramtype name="TDecoder">The type of the Ice decoder.</paramtype>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="response">The incoming response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="decodeFunc">The decode function for the return value.</param>
        /// <param name="_">The cancellation token.</param>
        /// <returns>The return value.</returns>
        public static ValueTask<T> ToReturnValueAsync<TDecoder, T>(
            this IncomingResponse response,
            IInvoker? invoker,
            IIceDecoderFactory<TDecoder> iceDecoderFactory,
            DecodeFunc<TDecoder, T> decodeFunc,
            CancellationToken _) where TDecoder : IceDecoder
        {
            if (response.PayloadEncoding != iceDecoderFactory.Encoding)
            {
                throw new InvalidDataException(@$"cannot decode response payload encoded with {response.PayloadEncoding
                    }; expected a payload encoded with {iceDecoderFactory.Encoding}");
            }

            TDecoder decoder = iceDecoderFactory.CreateIceDecoder(response.Payload, response.Connection, invoker);

            decoder.DecodeFixedLengthSize(); // skip return size for now

            if (response.ResultType == ResultType.Failure)
            {
                throw response.ToRemoteException(decoder);
            }
            else
            {
                T result = decodeFunc(decoder);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
                return new(result);
            }
        }

        /// <summary>Decodes a remote exception carried by a response.</summary>
        private static RemoteException ToRemoteException(this IncomingResponse response, IceDecoder decoder)
        {
            // the caller skipped the size

            RemoteException exception = decoder.DecodeException();

            if (exception is not UnknownSlicedRemoteException)
            {
                decoder.CheckEndOfBuffer(skipTaggedParams: false);
            }
            // else, we did not decode the full exception from the buffer

            return exception;
        }
    }
}
