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
            DefaultIceDecoderFactories defaultIceDecoderFactories)
        {
            if (request.PayloadEncoding is IceEncoding payloadEncoding)
            {
                payloadEncoding.GetIceDecoderFactory(request.Features, defaultIceDecoderFactories).
                    CreateIceDecoder(request.Payload, request.Connection, request.ProxyInvoker).
                        CheckEndOfBuffer(skipTaggedParams: true);
            }
            else
            {
                throw new NotSupportedException(
                    $"cannot decode payload of request {request.Operation} encoded with {request.PayloadEncoding}");
            }
        }

        /// <summary>The generated code calls this method to ensure that when an operation is _not_ declared
        /// idempotent, the request is not marked idempotent. If the request is marked idempotent, it means the caller
        /// incorrectly believes this operation is idempotent.</summary>
        public static void CheckNonIdempotent(this IncomingRequest request)
        {
            if (request.IsIdempotent)
            {
                throw new InvalidDataException(
                    $@"idempotent mismatch for operation '{request.Operation
                    }': received request marked idempotent for a non-idempotent operation");
            }
        }

        /// <summary>The generated code calls this method to ensure that streaming is aborted if the operation
        /// doesn't specify a stream parameter.</summary>
        public static void StreamReadingComplete(this IncomingRequest request) =>
            request.Stream.AbortRead(IceRpc.Transports.RpcStreamError.UnexpectedStreamData);

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
            if (request.PayloadEncoding is IceEncoding payloadEncoding)
            {
                IceDecoder decoder = payloadEncoding.GetIceDecoderFactory(
                    request.Features,
                    defaultIceDecoderFactories).CreateIceDecoder(request.Payload,
                                                                 request.Connection,
                                                                 request.ProxyInvoker);
                T result = decodeFunc(decoder);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
                return result;
            }
            else
            {
                throw new NotSupportedException(
                    $"cannot decode payload of request {request.Operation} encoded with {request.PayloadEncoding}");
            }
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
                throw new InvalidDataException(@$"cannot decode payload of request {request.Operation
                    } encoded with {request.PayloadEncoding
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
