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
    /// <summary>Decodes a response payload.</summary>
    /// <typeparam name="T">The type of the return value.</typeparam>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="encoding">The encoding of the response payload.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="defaultActivator">The activator to use when the activator of the Slice feature is null.</param>
    /// <param name="decodeFunc">The decode function for the return value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The return value.</returns>
    public static ValueTask<T> DecodeReturnValueAsync<T>(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        ServiceProxy sender,
        IActivator? defaultActivator,
        DecodeFunc<T> decodeFunc,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        IActivator? activator = feature.Activator ?? defaultActivator;

        return response.StatusCode == StatusCode.Success ?
            response.DecodeValueAsync(
                encoding,
                feature,
                activator,
                sender,
                decodeFunc,
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
            else
            {
                throw await response.DecodeRemoteExceptionAsync(
                    request,
                    encoding,
                    feature,
                    activator,
                    sender,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Verifies that a response payload carries no return value or only tagged return values.</summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="encoding">The encoding of the response payload.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="defaultActivator">The activator to use when the activator of the Slice sliceFeature is null.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A value task representing the asynchronous completion of the operation.</returns>
    public static ValueTask DecodeVoidReturnValueAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        ServiceProxy sender,
        IActivator? defaultActivator = null,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return response.StatusCode == StatusCode.Success ?
            response.DecodeVoidAsync(encoding, feature, cancellationToken) : ThrowExceptionAsync();

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
                throw await response.DecodeRemoteExceptionAsync(
                    request,
                    encoding,
                    feature,
                    feature.Activator ?? defaultActivator,
                    sender,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask<SliceException> DecodeRemoteExceptionAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        ISliceFeature feature,
        IActivator? activator,
        ServiceProxy sender,
        CancellationToken cancellationToken)
    {
        Debug.Assert(response.StatusCode == StatusCode.ApplicationError);

        ReadResult readResult = await response.Payload.ReadSegmentAsync(
            encoding,
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
                encoding,
                activator,
                feature.ServiceProxyFactory,
                sender,
                maxCollectionAllocation: feature.MaxCollectionAllocation,
                maxDepth: feature.MaxDepth);

            SliceException sliceException = encoding == SliceEncoding.Slice1 ?
                decoder.DecodeUserException() :
                decoder.DecodeTrait(CreateUnknownException);

            if (sliceException is not UnknownException)
            {
                decoder.CheckEndOfBuffer(skipTaggedParams: false);
            }
            // else, we did not decode the full exception from the buffer

            return sliceException;

            // If we can't decode this exception, we return an UnknownException with the undecodable exception's
            // type identifier and message.
            static SliceException CreateUnknownException(string typeId, ref SliceDecoder decoder) =>
                new UnknownException(typeId, decoder.DecodeString());
        }
    }
}
