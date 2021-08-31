// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Extension methods to decode the payloads of incoming requests when such payloads are encoded with the
    /// Ice encoding.</summary>
    public static class IncomingRequestExtensions
    {
        /// <summary>Verifies that a request payload carries no argument or only unknown tagged arguments.</summary>
        /// <param name="request">The incoming request.</param>
        /// <param name="defaultIceDecoderFactories">The default Ice decoder factories.</param>
        public static void CheckEmptyArgs(
            this IncomingRequest request,
            DefaultIceDecoderFactories defaultIceDecoderFactories) =>
            request.PayloadEncoding.GetIceDecoderFactory(request.Features, defaultIceDecoderFactories).
                CreateIceDecoder(request.Payload, request.Connection, request.ProxyInvoker).
                    CheckEndOfBuffer(skipTaggedParams: true);

        /// <summary>Decodes the request's payload into a list of arguments. The payload can be encoded using any Ice
        /// encoding.</summary>
        /// <paramtype name="T">The type of the request parameters.</paramtype>
        /// <param name="request">The incoming request.</param>
        /// <param name="defaultIceDecoderFactories">The default Ice decoder factories.</param>
        /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
        /// <returns>The request arguments.</returns>
        public static T ToArgs<T>(
            this IncomingRequest request,
            DefaultIceDecoderFactories defaultIceDecoderFactories,
            DecodeFunc<IceDecoder, T> decodeFunc)
        {
            IceDecoder decoder = request.PayloadEncoding.GetIceDecoderFactory(
                request.Features,
                defaultIceDecoderFactories).CreateIceDecoder(request.Payload, request.Connection, request.ProxyInvoker);
            T result = decodeFunc(decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: true);
            return result;
        }

        /// <summary>Decodes the request's payload into a list of arguments. The payload must be encoded with a specific
        /// Ice encoding.</summary>
        /// <paramtype name="TDecoder">The type of the Ice decoder.</paramtype>
        /// <paramtype name="T">The type of the request parameters.</paramtype>
        /// <param name="request">The incoming request.</param>
        /// <param name="defaultIceDecoderFactory">The default Ice decoder factory.</param>
        /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
        /// <returns>The request arguments.</returns>
        public static T ToArgs<TDecoder, T>(
            this IncomingRequest request,
            IIceDecoderFactory<TDecoder> defaultIceDecoderFactory,
            DecodeFunc<TDecoder, T> decodeFunc) where TDecoder : IceDecoder
        {
            if (request.PayloadEncoding != defaultIceDecoderFactory.Encoding)
            {
                throw new InvalidDataException(@$"cannot decode request payload encoded with {request.PayloadEncoding
                    }; expected a payload encoded with {defaultIceDecoderFactory.Encoding}");
            }

            TDecoder decoder = (request.Features.Get<IIceDecoderFactory<TDecoder>>() ?? defaultIceDecoderFactory).
                CreateIceDecoder(request.Payload, request.Connection, request.ProxyInvoker);
            T result = decodeFunc(decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: true);
            return result;
        }
    }
}
