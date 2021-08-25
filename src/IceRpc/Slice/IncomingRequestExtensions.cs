// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

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

        /// <summary>Decodes the request's payload into a list of arguments.</summary>
        /// <paramtype name="T">The type of the request parameters.</paramtype>
        /// <param name="request">The incoming request.</param>
        /// <param name="defaultIceDecoderFactories">The default Ice decoder factories.</param>
        /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
        /// <returns>The request arguments.</returns>
        public static T ToArgs<T>(
            this IncomingRequest request,
            DefaultIceDecoderFactories defaultIceDecoderFactories,
            DecodeFunc<T> decodeFunc)
        {
            var decoder = request.PayloadEncoding.GetIceDecoderFactory(request.Features, defaultIceDecoderFactories).
                CreateIceDecoder(request.Payload, request.Connection, request.ProxyInvoker);
            T result = decodeFunc(decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: true);
            return result;
        }
    }
}
