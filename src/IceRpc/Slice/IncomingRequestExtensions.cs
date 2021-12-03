// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>Extension methods to decode the payload of an incoming request when this payload is encoded with the
    /// Ice encoding.</summary>
    public static class IncomingRequestExtensions
    {
        /// <summary>Verifies that a request payload carries no argument or only unknown tagged arguments.</summary>
        /// <param name="request">The incoming request.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task that completes when the checking is complete.</returns>
        public static ValueTask CheckEmptyArgsAsync(
            this IncomingRequest request,
            IIceDecoderFactory<IceDecoder> iceDecoderFactory,
            CancellationToken cancel) => request.Payload.ReadVoidAsync(iceDecoderFactory, cancel);

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

        /// <summary>Computes the Ice encoding to use when encoding a Slice-generated response.</summary>
        public static IceEncoding GetIceEncoding(this IncomingRequest request) =>
            request.PayloadEncoding as IceEncoding ?? request.Protocol.IceEncoding ??
                throw new NotSupportedException($"unknown protocol {request.Protocol}");

        /// <summary>The generated code calls this method to ensure that streaming is aborted if the operation
        /// doesn't specify a stream parameter.</summary>
        public static void StreamReadingComplete(this IncomingRequest request) =>
            request.Stream?.AbortRead((byte)MultiplexedStreamError.UnexpectedStreamData);

        /// <summary>Decodes the request's payload into a list of arguments.</summary>
        /// <paramtype name="TDecoder">The type of the Ice decoder.</paramtype>
        /// <paramtype name="T">The type of the request parameters.</paramtype>
        /// <param name="request">The incoming request.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The request arguments.</returns>
        public static ValueTask<T> ToArgsAsync<TDecoder, T>(
            this IncomingRequest request,
            IIceDecoderFactory<TDecoder> iceDecoderFactory,
            DecodeFunc<TDecoder, T> decodeFunc,
            CancellationToken cancel) where TDecoder : IceDecoder =>
            request.Payload.ReadValueAsync(
                iceDecoderFactory,
                decodeFunc,
                request.Connection,
                request.ProxyInvoker,
                cancel);
    }
}
