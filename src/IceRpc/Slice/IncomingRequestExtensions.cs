// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Extension methods to decode the payload of an incoming request when this payload is encoded with the
/// Slice encoding.</summary>
public static class IncomingRequestExtensions
{
    /// <summary>The generated code calls this method to ensure that when an operation is _not_ declared
    /// idempotent, the request is not marked idempotent. If the request is marked idempotent, it means the caller
    /// incorrectly believes this operation is idempotent.</summary>
    /// <param name="request">The request to check.</param>
    public static void CheckNonIdempotent(this IncomingRequest request)
    {
        if (request.Fields.ContainsKey(RequestFieldKey.Idempotent))
        {
            throw new InvalidDataException(
                $"idempotent mismatch for operation '{request.Operation}': received request marked idempotent for a non-idempotent operation");
        }
    }

    /// <summary>Creates an outgoing response with status code <see cref="StatusCode.ApplicationError" /> with
    /// a Slice exception payload.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="sliceException">The Slice exception to encode in the payload.</param>
    /// <param name="encoding">The encoding used for the request payload.</param>
    /// <returns>The new outgoing response.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="sliceException" /> is a dispatch exception or
    /// its <see cref="DispatchException.ConvertToUnhandled" /> property is <see langword="true" />.</exception>
    public static OutgoingResponse CreateSliceExceptionResponse(
        this IncomingRequest request,
        SliceException sliceException,
        SliceEncoding encoding)
    {
        if (sliceException.ConvertToUnhandled)
        {
            throw new ArgumentException("invalid Slice exception", nameof(sliceException));
        }

        var response = new OutgoingResponse(request, StatusCode.ApplicationError, sliceException.Message)
        {
            Payload = CreateExceptionPayload()
        };

        if (response.Protocol.HasFields && sliceException.RetryPolicy != RetryPolicy.NoRetry)
        {
            // Encode the retry policy into the fields of the new response.
            RetryPolicy retryPolicy = sliceException.RetryPolicy;
            response.Fields = response.Fields.With(
                ResponseFieldKey.RetryPolicy,
                retryPolicy.Encode);
        }
        return response;

        PipeReader CreateExceptionPayload()
        {
            SliceEncodeOptions encodeOptions = request.Features.Get<ISliceFeature>()?.EncodeOptions ??
                SliceEncodeOptions.Default;

            var pipe = new Pipe(encodeOptions.PipeOptions);
            var encoder = new SliceEncoder(pipe.Writer, encoding);

            // Encode can throw if the exception does not support encoding.
            if (encoding == SliceEncoding.Slice1)
            {
                sliceException.Encode(ref encoder);
            }
            else
            {
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
                int startPos = encoder.EncodedByteCount;
                sliceException.Encode(ref encoder);
                SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
            }

            pipe.Writer.Complete(); // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }
    }

    /// <summary>Decodes a Slice1-encoded request payload into a list of arguments.</summary>
    /// <typeparam name="T">The type of the request parameters.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="defaultActivator">The activator to use when the activator of the Slice feature is null.</param>
    /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request arguments.</returns>
    public static ValueTask<T> DecodeArgsAsync<T>(
        this IncomingRequest request,
        IActivator? defaultActivator,
        DecodeFunc<T> decodeFunc,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return request.DecodeValueAsync(
            SliceEncoding.Slice1,
            feature,
            feature.Activator ?? defaultActivator,
            templateProxy: null,
            decodeFunc,
            cancellationToken);
    }

    /// <summary>Decodes a request payload into a list of arguments.</summary>
    /// <typeparam name="T">The type of the request parameters.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="encoding">The encoding of the request payload. Must be Slice2 or greater.</param>
    /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request arguments.</returns>
    public static ValueTask<T> DecodeArgsAsync<T>(
        this IncomingRequest request,
        SliceEncoding encoding,
        DecodeFunc<T> decodeFunc,
        CancellationToken cancellationToken = default) =>
        request.DecodeValueAsync(
            encoding,
            request.Features.Get<ISliceFeature>() ?? SliceFeature.Default,
            activator: null,
            templateProxy: null,
            decodeFunc,
            cancellationToken);

    /// <summary>Verifies that a request payload carries no argument or only unknown tagged arguments.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="encoding">The encoding of the request payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A value task that completes when the checking is complete.</returns>
    public static ValueTask DecodeEmptyArgsAsync(
        this IncomingRequest request,
        SliceEncoding encoding,
        CancellationToken cancellationToken = default) =>
        request.DecodeVoidAsync(
            encoding,
            request.Features.Get<ISliceFeature>() ?? SliceFeature.Default,
            cancellationToken);
}
