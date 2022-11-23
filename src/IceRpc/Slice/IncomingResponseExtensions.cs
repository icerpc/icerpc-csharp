// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Extension methods to decode the payloads of incoming responses when such payloads are encoded with the
/// Slice encoding.</summary>
public static class IncomingResponseExtensions
{
    /// <summary>Decodes a Slice1-encoded response payload.</summary>
    /// <typeparam name="T">The type of the return value.</typeparam>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="defaultActivator">The activator to use when the activator of the Slice feature is null.</param>
    /// <param name="decodeReturnValue">A function that decodes the return value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The return value.</returns>
    /// <exception cref="DispatchException">Thrown if the status code of the response is greater than
    /// <see cref="StatusCode.ApplicationError" />.</exception>
    public static ValueTask<T> DecodeReturnValueAsync<T>(
        this IncomingResponse response,
        OutgoingRequest request,
        ServiceProxy sender,
        IActivator? defaultActivator,
        DecodeFunc<T> decodeReturnValue,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        IActivator? activator = feature.Activator ?? defaultActivator;

        return response.StatusCode switch
        {
            StatusCode.Success => response.DecodeValueAsync(
                SliceEncoding.Slice1,
                feature,
                sender,
                decodeReturnValue,
                activator,
                cancellationToken),

            StatusCode.ApplicationError => DecodeAndThrowExceptionAsync(),

            _ => throw new DispatchException(response.StatusCode, response.ErrorMessage) { ConvertToUnhandled = true }
        };

        async ValueTask<T> DecodeAndThrowExceptionAsync() =>
            throw await response.DecodeSliceExceptionAsync(
                feature,
                activator,
                sender,
                cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Decodes a response payload.</summary>
    /// <typeparam name="T">The type of the return value.</typeparam>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="encoding">The encoding of the response payload. Must be Slice2 or greater.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="decodeReturnValue">A function that decodes the return value.</param>
    /// <param name="decodeException">A function that decodes the exception thrown by the operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The return value.</returns>
    /// <exception cref="ArgumentException">Throw if <paramref name="encoding" /> is
    /// <see cref="SliceEncoding.Slice1" />.</exception>
    /// <exception cref="DispatchException">Thrown if the status code of the response is greater than
    /// <see cref="StatusCode.ApplicationError" /> or is equal to <see cref="StatusCode.ApplicationError" /> and
    /// <paramref name="decodeException" /> is null.</exception>
    public static ValueTask<T> DecodeReturnValueAsync<T>(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        ServiceProxy sender,
        DecodeFunc<T> decodeReturnValue,
        DecodeExceptionFunc? decodeException = null,
        CancellationToken cancellationToken = default)
    {
        if (encoding == SliceEncoding.Slice1)
        {
            throw new ArgumentException(
                $"{nameof(DecodeReturnValueAsync)} is not compatible with the Slice1 encoding",
                nameof(encoding));
        }

        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return response.StatusCode switch
        {
            StatusCode.Success => response.DecodeValueAsync(
                encoding,
                feature,
                sender,
                decodeReturnValue,
                activator: null,
                cancellationToken),

            StatusCode.ApplicationError when decodeException is not null => DecodeAndThrowExceptionAsync(),

            _ => throw new DispatchException(response.StatusCode, response.ErrorMessage) { ConvertToUnhandled = true }
        };

        async ValueTask<T> DecodeAndThrowExceptionAsync() =>
            throw await response.DecodeSliceExceptionAsync(
                    encoding,
                    feature,
                    sender,
                    decodeException,
                    cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Verifies that a Slice1-encoded response payload carries no return value or only tagged return values.
    /// </summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="defaultActivator">The activator to use when the activator of the Slice sliceFeature is null.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A value task representing the asynchronous completion of the operation.</returns>
    /// <exception cref="DispatchException">Thrown if the status code of the response is greater than
    /// <see cref="StatusCode.ApplicationError" />.</exception>
    public static ValueTask DecodeVoidReturnValueAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        ServiceProxy sender,
        IActivator? defaultActivator = null,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return response.StatusCode switch
        {
            StatusCode.Success => response.DecodeVoidAsync(SliceEncoding.Slice1, feature, cancellationToken),
            StatusCode.ApplicationError => DecodeAndThrowExceptionAsync(),
            _ => throw new DispatchException(response.StatusCode, response.ErrorMessage) { ConvertToUnhandled = true }
        };

        async ValueTask DecodeAndThrowExceptionAsync() =>
            throw await response.DecodeSliceExceptionAsync(
                feature,
                feature.Activator ?? defaultActivator,
                sender,
                cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Verifies that a response payload carries no return value or only tagged return values.</summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="encoding">The encoding of the response payload. Must be Slice2 or greater.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="decodeException">A function that decodes the exception thrown by the operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A value task representing the asynchronous completion of the operation.</returns>
    /// <exception cref="ArgumentException">Throw if <paramref name="encoding" /> is
    /// <see cref="SliceEncoding.Slice1" />.</exception>
    /// <exception cref="DispatchException">Thrown if the status code of the response is greater than
    /// <see cref="StatusCode.ApplicationError" /> or is equal to <see cref="StatusCode.ApplicationError" /> and
    /// <paramref name="decodeException" /> is null.</exception>
    public static ValueTask DecodeVoidReturnValueAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        ServiceProxy sender,
        DecodeExceptionFunc? decodeException = null,
        CancellationToken cancellationToken = default)
    {
        if (encoding == SliceEncoding.Slice1)
        {
            throw new ArgumentException(
                $"{nameof(DecodeVoidReturnValueAsync)} is not compatible with the Slice1 encoding",
                nameof(encoding));
        }

        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return response.StatusCode switch
        {
            StatusCode.Success => response.DecodeVoidAsync(encoding, feature, cancellationToken),
            StatusCode.ApplicationError when decodeException is not null => DecodeAndThrowExceptionAsync(),
            _ => throw new DispatchException(response.StatusCode, response.ErrorMessage) { ConvertToUnhandled = true }
        };

        async ValueTask DecodeAndThrowExceptionAsync() =>
            throw await response.DecodeSliceExceptionAsync(
                encoding,
                feature,
                sender,
                decodeException,
                cancellationToken).ConfigureAwait(false);
    }

    // Slice1 only
    private static async ValueTask<SliceException> DecodeSliceExceptionAsync(
        this IncomingResponse response,
        ISliceFeature feature,
        IActivator? activator,
        ServiceProxy sender,
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
            throw new InvalidOperationException("unexpected call to CancelPendingRead on a response payload");
        }

        SliceException result = Decode(readResult.Buffer);
        response.Payload.AdvanceTo(readResult.Buffer.End);
        return result;

        SliceException Decode(ReadOnlySequence<byte> buffer)
        {
            var decoder = new SliceDecoder(
                buffer,
                SliceEncoding.Slice1,
                feature.ServiceProxyFactory,
                sender,
                maxCollectionAllocation: feature.MaxCollectionAllocation,
                activator,
                maxDepth: feature.MaxDepth);

            SliceException exception = decoder.DecodeUserException();
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return exception;
        }
    }

    // Slice2+
    private static async ValueTask<DispatchException> DecodeSliceExceptionAsync(
        this IncomingResponse response,
        SliceEncoding encoding,
        ISliceFeature feature,
        ServiceProxy sender,
        DecodeExceptionFunc decodeException,
        CancellationToken cancellationToken)
    {
        Debug.Assert(response.StatusCode == StatusCode.ApplicationError);
        Debug.Assert(encoding != SliceEncoding.Slice1);

        ReadResult readResult = await response.Payload.ReadSegmentAsync(
            encoding,
            feature.MaxSegmentSize,
            cancellationToken).ConfigureAwait(false);

        // We never call CancelPendingRead on response.Payload; an interceptor can but it's not correct.
        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("unexpected call to CancelPendingRead on a response payload");
        }

        // If the error message is empty, we use the default System error message. This would typically happen with
        // a Slice2 exception received over ice.
        string? errorMessage = response.ErrorMessage!.Length == 0 ? null : response.ErrorMessage;

        if (readResult.Buffer.IsEmpty)
        {
            // The payload is empty, no need to decode it.
            // Note that a Slice2-encoded exception uses at least 1 byte for tags.
            return new DispatchException(response.StatusCode, errorMessage)
            {
                ConvertToUnhandled = true
            };
        }
        else
        {
            SliceException exception = Decode(readResult.Buffer);
            response.Payload.AdvanceTo(readResult.Buffer.End);
            return exception;

            SliceException Decode(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(
                    buffer,
                    encoding,
                    feature.ServiceProxyFactory,
                    sender,
                    maxCollectionAllocation: feature.MaxCollectionAllocation);

                try
                {
                    SliceException sliceException = decodeException(errorMessage, ref decoder);
                    decoder.CheckEndOfBuffer(skipTaggedParams: false);
                    return sliceException;
                }
                catch (InvalidDataException exception)
                {
                    throw new InvalidDataException(
                        $"failed to decode Slice exception from response {{ Message = {errorMessage} }}",
                        exception);
                }
            }
        }
    }
}
