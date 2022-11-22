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

        return response.StatusCode == StatusCode.Success ?
            response.DecodeValueAsync(
                SliceEncoding.Slice1,
                feature,
                activator,
                sender,
                decodeReturnValue,
                cancellationToken) :
            ThrowExceptionAsync();

        async ValueTask<T> ThrowExceptionAsync()
        {
            if (response.StatusCode > StatusCode.ApplicationError)
            {
                throw new DispatchException(response.StatusCode, response.ErrorMessage)
                {
                    ConvertToUnhandled = true
                };
            }
            {
                throw await response.DecodeSliceExceptionAsync(
                    feature,
                    activator,
                    sender,
                    cancellationToken).ConfigureAwait(false);
            }
        }
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

        return response.StatusCode == StatusCode.Success ?
            response.DecodeValueAsync(
                encoding,
                feature,
                activator: null,
                sender,
                decodeReturnValue,
                cancellationToken) :
            ThrowExceptionAsync();

        async ValueTask<T> ThrowExceptionAsync()
        {
            if (response.StatusCode > StatusCode.ApplicationError || decodeException is null)
            {
                throw new DispatchException(response.StatusCode, response.ErrorMessage)
                {
                    ConvertToUnhandled = true
                };
            }
            else
            {
                throw await response.DecodeSliceExceptionAsync(
                    encoding,
                    feature,
                    sender,
                    decodeException,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Verifies that a response payload carries no return value or only tagged return values.</summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="defaultActivator">The activator to use when the activator of the Slice sliceFeature is null.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A value task representing the asynchronous completion of the operation.</returns>
    public static ValueTask DecodeVoidReturnValueAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        ServiceProxy sender,
        IActivator? defaultActivator = null,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return response.StatusCode == StatusCode.Success ?
            response.DecodeVoidAsync(SliceEncoding.Slice1, feature, cancellationToken) : ThrowExceptionAsync();

        async ValueTask ThrowExceptionAsync()
        {
            if (response.StatusCode > StatusCode.ApplicationError)
            {
                throw new DispatchException(response.StatusCode, response.ErrorMessage)
                {
                    ConvertToUnhandled = true
                };
            }
            else
            {
                throw await response.DecodeSliceExceptionAsync(
                    feature,
                    feature.Activator ?? defaultActivator,
                    sender,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Verifies that a response payload carries no return value or only tagged return values.</summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="encoding">The encoding of the response payload. Must be Slice2 or greater.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="decodeException">A function that decodes the exception thrown by the operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A value task representing the asynchronous completion of the operation.</returns>
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

        return response.StatusCode == StatusCode.Success ?
            response.DecodeVoidAsync(encoding, feature, cancellationToken) : ThrowExceptionAsync();

        async ValueTask ThrowExceptionAsync()
        {
            if (response.StatusCode > StatusCode.ApplicationError || decodeException is null)
            {
                throw new DispatchException(response.StatusCode, response.ErrorMessage)
                {
                    ConvertToUnhandled = true
                };
            }
            else
            {
                throw await response.DecodeSliceExceptionAsync(
                    encoding,
                    feature,
                    sender,
                    decodeException,
                    cancellationToken).ConfigureAwait(false);
            }
        }
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
                activator,
                feature.ServiceProxyFactory,
                sender,
                maxCollectionAllocation: feature.MaxCollectionAllocation,
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
            SliceEncoding.Slice2,
            feature.MaxSegmentSize,
            cancellationToken).ConfigureAwait(false);

        // We never call CancelPendingRead on response.Payload; an interceptor can but it's not correct.
        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("unexpected call to CancelPendingRead on a response payload");
        }

        if (readResult.Buffer.IsEmpty)
        {
            return new DispatchException(response.StatusCode, response.ErrorMessage)
            {
                ConvertToUnhandled = true
            };
        }
        else
        {
            SliceException sliceException = Decode(readResult.Buffer);
            response.Payload.AdvanceTo(readResult.Buffer.End);
            return sliceException;

            SliceException Decode(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(
                    buffer,
                    encoding,
                    activator: null,
                    feature.ServiceProxyFactory,
                    sender,
                    maxCollectionAllocation: feature.MaxCollectionAllocation,
                    maxDepth: feature.MaxDepth);

                try
                {
                    SliceException sliceException = decodeException(ref decoder, response.ErrorMessage!);
                    decoder.CheckEndOfBuffer(skipTaggedParams: false);
                    return sliceException;
                }
                catch (InvalidDataException exception)
                {
                    throw new InvalidDataException(
                        $"failed to decode Slice exception from response {{ Message = {response.ErrorMessage} }}",
                        exception);
                }
            }
        }
    }
}
