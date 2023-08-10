// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using ZeroC.Slice;

namespace IceRpc.Slice;

/// <summary>Provides extension methods for <see cref="IncomingResponse" /> to decode its Slice-encoded payload.
/// </summary>
public static class IncomingResponseExtensions
{
    /// <summary>Decodes a response payload.</summary>
    /// <typeparam name="T">The type of the return value.</typeparam>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="encoding">The encoding of the response payload.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="decodeReturnValue">A function that decodes the return value.</param>
    /// <param name="defaultActivator">The activator to use when the activator provided by the request's <see
    /// cref="ISliceFeature" /> is <see langword="null" />. Used only when <paramref name="encoding" /> is <see
    /// cref="SliceEncoding.Slice1" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the operation's return value. This value tasks holds a <see
    /// cref="SliceException" /> when encoding is Slice1 and the status code is
    /// <see cref="StatusCode.ApplicationError"/>.</returns>
    /// <exception cref="DispatchException">Thrown when the encoding is Slice1 and the status code of the response is
    /// greater than <see cref="StatusCode.ApplicationError"/>, or when the encoding is Slice2 and the status code is
    /// greater than <see cref="StatusCode.Ok"/>.</exception>
    public static ValueTask<T> DecodeReturnValueAsync<T>(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        GenericProxy sender,
        DecodeFunc<T> decodeReturnValue,
        IActivator? defaultActivator = null,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;
        IActivator? activator = feature.Activator ?? defaultActivator;

        return response.StatusCode switch
        {
            StatusCode.Ok => response.DecodeValueAsync(
                encoding,
                feature,
                feature.ProxyFactory ?? sender.With,
                decodeReturnValue,
                activator,
                cancellationToken),

            StatusCode.ApplicationError when encoding == SliceEncoding.Slice1 => DecodeAndThrowExceptionAsync(),

            _ => throw new DispatchException(response.StatusCode, response.ErrorMessage)
            {
                ConvertToInternalError = true
            }
        };

        async ValueTask<T> DecodeAndThrowExceptionAsync() =>
            throw await response.DecodeSliceExceptionAsync(feature, sender, activator, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>Verifies that a Slice-encoded response payload carries no return value or only tagged return values.
    /// </summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="encoding">The encoding of the response payload.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="defaultActivator">The activator to use when the activator provided by the request's <see
    /// cref="ISliceFeature" /> is <see langword="null" />. Used only when <paramref name="encoding" /> is <see
    /// cref="SliceEncoding.Slice1" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task representing the asynchronous completion of the operation. This value tasks holds a <see
    /// cref="SliceException" /> when encoding is Slice1 and the status code is
    /// <see cref="StatusCode.ApplicationError" />.</returns>
    /// <exception cref="DispatchException">Thrown when the encoding is Slice1 and the status code of the response is
    /// greater than <see cref="StatusCode.ApplicationError"/>, or when the encoding is Slice2 and the status code is
    /// greater than <see cref="StatusCode.Ok"/>.</exception>
    public static ValueTask DecodeVoidReturnValueAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        GenericProxy sender,
        IActivator? defaultActivator = null,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;
        IActivator? activator = defaultActivator ?? feature.Activator;

        return response.StatusCode switch
        {
            StatusCode.Ok => response.DecodeVoidAsync(encoding, feature, cancellationToken),
            StatusCode.ApplicationError when encoding == SliceEncoding.Slice1 => DecodeAndThrowExceptionAsync(),
            _ => throw new DispatchException(response.StatusCode, response.ErrorMessage)
            {
                ConvertToInternalError = true
            }
        };

        async ValueTask DecodeAndThrowExceptionAsync() =>
            throw await response.DecodeSliceExceptionAsync(feature, sender, activator, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>Verifies that a Slice2-encoded response payload carries no return value or only tagged return values.
    /// </summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="sender">The proxy that sent the request (not used by this method).</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task representing the asynchronous completion of the operation.</returns>
    /// <exception cref="DispatchException">Thrown if the status code of the response is greater than <see
    /// cref="StatusCode.Ok" />.</exception>
    /// <remarks>This method is a <see cref="ResponseDecodeFunc" />.</remarks>
    public static ValueTask DecodeVoidReturnValueAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        GenericProxy sender = default,
        CancellationToken cancellationToken = default) =>
        response.DecodeVoidReturnValueAsync(request, SliceEncoding.Slice2, sender, null, cancellationToken);

    // Slice1-only
    private static async ValueTask<SliceException> DecodeSliceExceptionAsync(
        this IncomingResponse response,
        ISliceFeature feature,
        GenericProxy sender,
        IActivator? activator,
        CancellationToken cancellationToken)
    {
        Debug.Assert(response.StatusCode == StatusCode.ApplicationError);

        ReadResult readResult = await response.Payload.ReadSegmentAsync(
            SliceEncoding.Slice1,
            feature.MaxSegmentSize,
            cancellationToken).ConfigureAwait(false);

        // We never call CancelPendingRead on response.Payload; an interceptor can but it's not correct.
        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("Unexpected call to CancelPendingRead.");
        }

        SliceException exception = DecodeBuffer(readResult.Buffer);
        response.Payload.AdvanceTo(readResult.Buffer.End);
        return exception;

        SliceException DecodeBuffer(ReadOnlySequence<byte> buffer)
        {
            // If the error message is empty, we switch to null to get the default System exception message. This would
            // typically happen when the Slice exception is received over ice.
            string? errorMessage = response.ErrorMessage!.Length == 0 ? null : response.ErrorMessage;

            var decoder = new SliceDecoder(
                buffer,
                SliceEncoding.Slice1,
                feature.ProxyFactory ?? sender.With,
                maxCollectionAllocation: feature.MaxCollectionAllocation,
                activator,
                maxDepth: feature.MaxDepth);

            SliceException exception = decoder.DecodeException(errorMessage);
            decoder.CheckEndOfBuffer();
            return exception;
        }
    }
}
