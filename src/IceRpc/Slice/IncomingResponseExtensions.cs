// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Provides extension methods to decode the Slice-encoded payload of an <see cref="IncomingResponse" />.
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
    /// <param name="decodeException">A function that decodes the exception thrown by the operation. Used only when
    /// <paramref name="encoding" /> is not <see cref="SliceEncoding.Slice1" />.</param>
    /// <param name="defaultActivator">The activator to use when the activator provided by the request's <see
    /// cref="ISliceFeature" /> is <see langword="null" />. Used only when <paramref name="encoding" /> is <see
    /// cref="SliceEncoding.Slice1" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The return value.</returns>
    /// <exception cref="DispatchException">Thrown if the status code of the response is greater than <see
    /// cref="StatusCode.ApplicationError" />. When both <paramref name="decodeException" /> is <see langword="null" />
    /// and <paramref name="encoding" /> is not <see cref="SliceEncoding.Slice1" />, it is also thrown for status code
    /// <see cref="StatusCode.ApplicationError" />.</exception>
    public static ValueTask<T> DecodeReturnValueAsync<T>(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        GenericProxy sender,
        DecodeFunc<T> decodeReturnValue,
        DecodeExceptionFunc? decodeException = null,
        IActivator? defaultActivator = null,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;
        IActivator? activator = feature.Activator ?? defaultActivator;

        return response.StatusCode switch
        {
            StatusCode.Success => response.DecodeValueAsync(
                encoding,
                feature,
                sender,
                decodeReturnValue,
                activator,
                cancellationToken),

            StatusCode.ApplicationError when decodeException is not null || encoding == SliceEncoding.Slice1 =>
                DecodeAndThrowExceptionAsync(),

            _ => throw new DispatchException(response.StatusCode, response.ErrorMessage) { ConvertToUnhandled = true }
        };

        async ValueTask<T> DecodeAndThrowExceptionAsync() =>
            throw await response.DecodeSliceExceptionAsync(
                encoding,
                feature,
                sender,
                decodeException,
                activator,
                cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Verifies that a Slice2 response payload carries no return value or only tagged return values.
    /// </summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task representing the asynchronous completion of the operation.</returns>
    /// <exception cref="DispatchException">Thrown if the status code of the response is greater or equal than <see
    /// cref="StatusCode.ApplicationError" />.</exception>
    /// <remarks>Use this method only with an operation with no exception specification. This method is a
    /// <see cref="ResponseDecodeFunc" />.</remarks>
    public static ValueTask DecodeVoidReturnValueAsync(
        IncomingResponse response,
        OutgoingRequest request,
        GenericProxy sender,
        CancellationToken cancellationToken = default) =>
        DecodeVoidReturnValueAsync(
            response,
            request,
            SliceEncoding.Slice2,
            sender,
            cancellationToken: cancellationToken);

    /// <summary>Verifies that a response payload carries no return value or only tagged return values.</summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="encoding">The encoding of the response payload.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="decodeException">A function that decodes the exception thrown by the operation. Used only when
    /// <paramref name="encoding" /> is not <see cref="SliceEncoding.Slice1" />.</param>
    /// <param name="defaultActivator">The activator to use when the activator provided by the request's <see
    /// cref="ISliceFeature" /> is <see langword="null" />. Used only when <paramref name="encoding" /> is <see
    /// cref="SliceEncoding.Slice1" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task representing the asynchronous completion of the operation.</returns>
    /// <exception cref="DispatchException">Thrown if the status code of the response is greater than <see
    /// cref="StatusCode.ApplicationError" />. When both <paramref name="decodeException" /> is <see langword="null" />
    /// and <paramref name="encoding" /> is not <see cref="SliceEncoding.Slice1" />, it is also thrown for status code
    /// <see cref="StatusCode.ApplicationError" />.</exception>
    public static ValueTask DecodeVoidReturnValueAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        GenericProxy sender,
        DecodeExceptionFunc? decodeException = null,
        IActivator? defaultActivator = null,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;
        IActivator? activator = defaultActivator ?? feature.Activator;

        return response.StatusCode switch
        {
            StatusCode.Success => response.DecodeVoidAsync(encoding, feature, cancellationToken),
            StatusCode.ApplicationError when decodeException is not null || encoding == SliceEncoding.Slice1 =>
                DecodeAndThrowExceptionAsync(),
            _ => throw new DispatchException(response.StatusCode, response.ErrorMessage) { ConvertToUnhandled = true }
        };

        async ValueTask DecodeAndThrowExceptionAsync() =>
            throw await response.DecodeSliceExceptionAsync(
                encoding,
                feature,
                sender,
                decodeException,
                activator,
                cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<SliceException> DecodeSliceExceptionAsync(
        this IncomingResponse response,
        SliceEncoding encoding,
        ISliceFeature feature,
        GenericProxy sender,
        DecodeExceptionFunc? decodeException,
        IActivator? activator,
        CancellationToken cancellationToken)
    {
        Debug.Assert(response.StatusCode == StatusCode.ApplicationError);
        Debug.Assert(encoding == SliceEncoding.Slice1 || decodeException is not null);

        ReadResult readResult = await response.Payload.ReadSegmentAsync(
            encoding,
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

            if (encoding == SliceEncoding.Slice1)
            {
                var decoder = new SliceDecoder(
                    buffer,
                    encoding,
                    feature.ProxyFactory,
                    sender,
                    maxCollectionAllocation: feature.MaxCollectionAllocation,
                    activator,
                    maxDepth: feature.MaxDepth);

                SliceException exception = decoder.DecodeUserException(errorMessage);
                if (exception is not UnknownSliceException)
                {
                    decoder.CheckEndOfBuffer(skipTaggedParams: false);
                }
                return exception;
            }
            else
            {
                var decoder = new SliceDecoder(
                    buffer,
                    encoding,
                    feature.ProxyFactory,
                    sender,
                    maxCollectionAllocation: feature.MaxCollectionAllocation);

                try
                {
                    SliceException sliceException = decodeException!(ref decoder, errorMessage);
                    decoder.CheckEndOfBuffer(skipTaggedParams: false);
                    return sliceException;
                }
                catch (InvalidDataException exception)
                {
                    throw new InvalidDataException(
                        $"Failed to decode Slice exception from response {{ Message = {errorMessage} }}.",
                        exception);
                }
            }
        }
    }
}
