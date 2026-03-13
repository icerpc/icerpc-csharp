// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Slice.Operations.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using ZeroC.Slice.Codec;

namespace IceRpc.Slice.Operations;

/// <summary>Provides extension methods for <see cref="IncomingResponse" /> to decode its Slice-encoded payload.
/// </summary>
public static class IncomingResponseExtensions
{
    /// <summary>Decodes a response payload.</summary>
    /// <typeparam name="T">The type of the return value.</typeparam>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="sender">The proxy that sent the request.</param>
    /// <param name="decodeReturnValue">A function that decodes the return value.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the operation's return value.</returns>
    /// <exception cref="DispatchException">Thrown when the status code is not <see cref="StatusCode.Ok"/>.</exception>
    public static ValueTask<T> DecodeReturnValueAsync<T>(
        this IncomingResponse response,
        OutgoingRequest request,
        ISliceProxy sender,
        DecodeFunc<T> decodeReturnValue,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return response.StatusCode == StatusCode.Ok ?
            response.DecodeValueAsync(
                    feature,
                    feature.BaseProxy ?? sender,
                    decodeReturnValue,
                    cancellationToken) :
            throw new DispatchException(response.StatusCode, response.ErrorMessage)
            {
                ConvertToInternalError = true
            };
    }

    /// <summary>Verifies that a Slice-encoded response payload carries no return value or only tagged return values.
    /// </summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task representing the asynchronous completion of the operation.</returns>
    /// <exception cref="DispatchException">Thrown when the status code is not <see cref="StatusCode.Ok"/>.</exception>
    public static ValueTask DecodeVoidReturnValueAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return response.StatusCode == StatusCode.Ok ?
            response.DecodeVoidAsync(feature, cancellationToken) :
            throw new DispatchException(response.StatusCode, response.ErrorMessage)
            {
                ConvertToInternalError = true
            };
    }
}
