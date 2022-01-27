// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>Extension methods to decode the payload of an incoming request when this payload is encoded with the
    /// Ice encoding.</summary>
    public static class IncomingRequestExtensions
    {
        /// <summary>Verifies that a request payload carries no argument or only unknown tagged arguments.</summary>
        /// <param name="request">The incoming request.</param>
        /// <param name="hasStream"><c>true</c> if this void value is followed by a stream parameter;
        /// otherwise, <c>false</c>.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task that completes when the checking is complete.</returns>
        public static ValueTask CheckEmptyArgsAsync(
            this IncomingRequest request,
            bool hasStream,
            CancellationToken cancel) => request.Payload.ReadVoidAsync(request.GetSlicePayloadEncoding(), hasStream, cancel);

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
            request.PayloadEncoding as IceEncoding ?? request.Protocol.SliceEncoding!;

        /// <summary>Decodes the request's payload into a list of arguments.</summary>
        /// <paramtype name="T">The type of the request parameters.</paramtype>
        /// <param name="request">The incoming request.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
        /// <param name="hasStream">When true, T is or includes a stream.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The request arguments.</returns>
        public static ValueTask<T> ToArgsAsync<T>(
            this IncomingRequest request,
            IActivator defaultActivator,
            DecodeFunc<T> decodeFunc,
            bool hasStream,
            CancellationToken cancel) =>
            request.Payload.ReadValueAsync(
                request.GetSlicePayloadEncoding(),
                request.Connection,
                request.ProxyInvoker,
                request.Features.Get<IActivator>() ?? defaultActivator,
                request.Features.GetSliceDecoderMaxDepth(),
                decodeFunc,
                hasStream,
                cancel);
    }
}
