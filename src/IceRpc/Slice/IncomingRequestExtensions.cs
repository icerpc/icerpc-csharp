// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;

namespace IceRpc.Slice
{
    /// <summary>Extension methods to decode the payload of an incoming request when this payload is encoded with the
    /// Slice encoding.</summary>
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
            CancellationToken cancel) =>
            request.DecodeVoidAsync(request.GetSliceEncoding(), hasStream, cancel);

        /// <summary>The generated code calls this method to ensure that when an operation is _not_ declared
        /// idempotent, the request is not marked idempotent. If the request is marked idempotent, it means the caller
        /// incorrectly believes this operation is idempotent.</summary>
        public static void CheckNonIdempotent(this IncomingRequest request)
        {
            if (request.Fields.ContainsKey((int)FieldKey.Idempotent))
            {
                throw new InvalidDataException(
                    $@"idempotent mismatch for operation '{request.Operation
                    }': received request marked idempotent for a non-idempotent operation");
            }
        }

        /// <summary>Creates an outgoing response from a remote exception.</summary>
        /// <param name="request">The incoming request.</param>
        /// <param name="remoteException">The remote exception to encode in the payload.</param>
        /// <param name="requestPayloadEncoding">The encoding used for the request payload.</param>
        /// <returns>An outgoing response with a <see cref="SliceResultType.ServiceFailure"/> result type.</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="remoteException"/> is a dispatch exception or
        /// its <see cref="RemoteException.ConvertToUnhandled"/> property is <c>true</c>.</exception>
        public static OutgoingResponse CreateResponseFromRemoteException(
            this IncomingRequest request,
            RemoteException remoteException,
            SliceEncoding requestPayloadEncoding)
        {
            if (remoteException is DispatchException || remoteException.ConvertToUnhandled)
            {
                throw new ArgumentException("invalid remote exception", nameof(remoteException));
            }

            var response = new OutgoingResponse(request)
            {
                ResultType = (ResultType)SliceResultType.ServiceFailure,
                PayloadSource = requestPayloadEncoding.CreatePayloadFromRemoteException(remoteException)
            };

            if (response.Protocol.HasFields && remoteException.RetryPolicy != RetryPolicy.NoRetry)
            {
                RetryPolicy retryPolicy = remoteException.RetryPolicy;
                response.Fields = response.Fields.With(
                    (int)FieldKey.RetryPolicy,
                    (ref SliceEncoder encoder) => retryPolicy.Encode(ref encoder));
            }
            return response;
        }

        /// <summary>Computes the Slice encoding to use when encoding a Slice-generated response.</summary>
        public static SliceEncoding GetSliceEncoding(this IncomingRequest request) =>
            request.PayloadEncoding as SliceEncoding ?? request.Protocol.SliceEncoding!;

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
            request.DecodeValueAsync(
                request.GetSliceEncoding(),
                request.Features.Get<SliceDecodePayloadOptions>() ?? SliceDecodePayloadOptions.Default,
                defaultActivator,
                defaultInvoker: Proxy.DefaultInvoker,
                decodeFunc,
                hasStream,
                cancel);

        /// <summary>Creates an async enumerable over the payload reader of an incoming request to decode streamed
        /// members.</summary>
        /// <param name="request">The incoming request.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="decodeFunc">The function used to decode the streamed member.</param>
        /// <returns>The async enumerable to decode and return the streamed members.</returns>
        public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            this IncomingRequest request,
            IActivator defaultActivator,
            DecodeFunc<T> decodeFunc) =>
            request.ToAsyncEnumerable(
                request.GetSliceEncoding(),
                request.Features.Get<SliceDecodePayloadOptions>() ?? SliceDecodePayloadOptions.Default,
                defaultActivator,
                defaultInvoker: Proxy.DefaultInvoker,
                decodeFunc);
    }
}
