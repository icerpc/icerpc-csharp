// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Ice.Codec;
using IceRpc.Ice.Operations.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Ice.Operations;

/// <summary>Provides extension methods for <see cref="IncomingResponse" /> to decode its Ice-encoded payload.
/// </summary>
public static class IncomingResponseExtensions
{
    /// <summary>Decodes a response payload.</summary>
    /// <typeparam name="T">The type of the return value.</typeparam>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="decodeReturnValue">A function that decodes the return value.</param>
    /// <param name="defaultActivator">The activator to use when the activator provided by the request's
    /// <see cref="IIceFeature" /> is <see langword="null" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the operation's return value. This value task is faulted and holds a <see
    /// cref="IceException" /> when the status code is <see cref="StatusCode.ApplicationError"/>.</returns>
    /// <exception cref="DispatchException">Thrown when the status code of the response is greater than
    /// <see cref="StatusCode.ApplicationError"/>.</exception>
    public static ValueTask<T> DecodeReturnValueAsync<T>(
        this IncomingResponse response,
        OutgoingRequest request,
        IIceProxy sender,
        DecodeFunc<T> decodeReturnValue,
        IActivator defaultActivator,
        CancellationToken cancellationToken = default)
    {
        IIceFeature feature = request.Features.Get<IIceFeature>() ?? IceFeature.Default;
        IActivator activator = feature.Activator ?? defaultActivator;

        return response.StatusCode switch
        {
            StatusCode.Ok => response.DecodeValueAsync(
                feature,
                feature.BaseProxy ?? sender,
                decodeReturnValue,
                activator,
                cancellationToken),

            StatusCode.ApplicationError => DecodeAndThrowExceptionAsync(),

            _ => throw new DispatchException(response.StatusCode, response.ErrorMessage)
            {
                ConvertToInternalError = true
            }
        };

        async ValueTask<T> DecodeAndThrowExceptionAsync() =>
            throw await response.DecodeIceExceptionAsync(feature, sender, activator, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>Verifies that an Ice-encoded response payload carries no return value or only tagged return values.
    /// </summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="defaultActivator">The activator to use when the activator provided by the request's
    /// <see cref="IIceFeature" /> is <see langword="null" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task representing the asynchronous completion of the operation. This value task is faulted and
    /// holds a <see cref="IceException" /> when the status code is <see cref="StatusCode.ApplicationError" />.
    /// </returns>
    /// <exception cref="DispatchException">Thrown when the status code of the response is greater than
    /// <see cref="StatusCode.ApplicationError"/>.</exception>
    public static ValueTask DecodeVoidReturnValueAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        IIceProxy sender,
        IActivator defaultActivator,
        CancellationToken cancellationToken = default)
    {
        IIceFeature feature = request.Features.Get<IIceFeature>() ?? IceFeature.Default;
        IActivator activator = feature.Activator ?? defaultActivator;

        return response.StatusCode switch
        {
            StatusCode.Ok => response.DecodeVoidAsync(feature, cancellationToken),
            StatusCode.ApplicationError => DecodeAndThrowExceptionAsync(),
            _ => throw new DispatchException(response.StatusCode, response.ErrorMessage)
            {
                ConvertToInternalError = true
            }
        };

        async ValueTask DecodeAndThrowExceptionAsync() =>
            throw await response.DecodeIceExceptionAsync(feature, sender, activator, cancellationToken)
                .ConfigureAwait(false);
    }

    private static async ValueTask<IceException> DecodeIceExceptionAsync(
        this IncomingResponse response,
        IIceFeature feature,
        IIceProxy sender,
        IActivator activator,
        CancellationToken cancellationToken)
    {
        Debug.Assert(response.StatusCode == StatusCode.ApplicationError);

        ReadResult readResult = await response.Payload.ReadFullPayloadAsync(
            feature.MaxPayloadSize,
            cancellationToken).ConfigureAwait(false);

        // We never call CancelPendingRead on response.Payload; an interceptor can but it's not correct.
        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("Unexpected call to CancelPendingRead.");
        }

        IceException exception = DecodeBuffer(readResult.Buffer);
        response.Payload.AdvanceTo(readResult.Buffer.End);
        return exception;

        IceException DecodeBuffer(ReadOnlySequence<byte> buffer)
        {
            // An Ice exception never sets Message, even when received over icerpc.

            var decoder = new IceDecoder(
                buffer,
                feature.BaseProxy ?? sender,
                maxCollectionAllocation: feature.MaxCollectionAllocation,
                activator,
                maxDepth: feature.MaxDepth);

            IceException exception = decoder.DecodeException(response.ErrorMessage);
            decoder.CheckEndOfBuffer();
            return exception;
        }
    }
}
