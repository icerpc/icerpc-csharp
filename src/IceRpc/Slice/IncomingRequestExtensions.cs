// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Internal;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Provides extension methods to decode the Slice-encoded payload of an <see cref="IncomingRequest" />.
/// </summary>
public static class IncomingRequestExtensions
{
    /// <summary>The generated code calls this method to ensure that when an operation is not declared idempotent,
    /// the request is not marked idempotent. If the request is marked idempotent, it means the caller incorrectly
    /// believes this operation is idempotent.</summary>
    /// <param name="request">The request to check.</param>
    public static void CheckNonIdempotent(this IncomingRequest request)
    {
        if (request.Fields.ContainsKey(RequestFieldKey.Idempotent))
        {
            throw new InvalidDataException(
                $"Invocation mode mismatch for operation '{request.Operation}': received idempotent field for an operation not marked as idempotent.");
        }
    }

    /// <summary>Creates an outgoing response with status code <see cref="StatusCode.ApplicationError" /> with
    /// a Slice exception payload.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="sliceException">The Slice exception to encode in the payload.</param>
    /// <param name="encoding">The encoding used for the request payload.</param>
    /// <returns>The new outgoing response.</returns>
    /// <exception cref="ArgumentException">Thrown if the <see cref="DispatchException.ConvertToUnhandled" /> property
    /// of <paramref name="sliceException" /> is <see langword="true" />.</exception>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="sliceException" /> does not support encoding
    /// <paramref name="encoding" />.</exception>
    public static OutgoingResponse CreateSliceExceptionResponse(
        this IncomingRequest request,
        SliceException sliceException,
        SliceEncoding encoding)
    {
        if (sliceException.ConvertToUnhandled)
        {
            throw new ArgumentException(
                "Cannot create a response using an slice exception that has 'ConvertToUnhandled' property set to true.",
                nameof(sliceException));
        }

        SliceEncodeOptions encodeOptions =
            request.Features.Get<ISliceFeature>()?.EncodeOptions ?? SliceEncodeOptions.Default;

        var pipe = new Pipe(encodeOptions.PipeOptions);

        try
        {
            var encoder = new SliceEncoder(pipe.Writer, encoding);

            // sliceException.Encode can throw NotSupportedException
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

            pipe.Writer.Complete();

            return new OutgoingResponse(request, sliceException) { Payload = pipe.Reader };
        }
        catch
        {
            pipe.Reader.Complete();
            pipe.Writer.Complete();
            throw;
        }
    }

    /// <summary>Decodes a request payload into a list of arguments.</summary>
    /// <typeparam name="T">The type of the request parameters.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="encoding">The encoding of the request's payload.</param>
    /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
    /// <param name="defaultActivator">The activator to use when the activator provided by the request's <see
    /// cref="ISliceFeature" /> is <see langword="null" />. Used only when <paramref name="encoding" /> is <see
    /// cref="SliceEncoding.Slice1" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The request arguments.</returns>
    public static ValueTask<T> DecodeArgsAsync<T>(
        this IncomingRequest request,
        SliceEncoding encoding,
        DecodeFunc<T> decodeFunc,
        IActivator? defaultActivator = null,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return request.DecodeValueAsync(
            encoding,
            feature,
            templateProxy: null,
            decodeFunc,
            feature.Activator ?? defaultActivator,
            cancellationToken);
    }

    /// <summary>Verifies that a request payload carries no argument or only unknown tagged arguments.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="encoding">The encoding of the request payload.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
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
