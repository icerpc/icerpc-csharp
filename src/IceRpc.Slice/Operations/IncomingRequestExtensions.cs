// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice.Operations.Internal;
using System.IO.Pipelines;
using ZeroC.Slice.Codec;

namespace IceRpc.Slice.Operations;

/// <summary>Provides extension methods for <see cref="IncomingRequest" /> to decode its Slice-encoded payload.
/// </summary>
public static class IncomingRequestExtensions
{
    /// <summary>Decodes a request payload into a list of arguments.</summary>
    /// <typeparam name="T">The type of the request parameters.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The request arguments.</returns>
    public static ValueTask<T> DecodeArgsAsync<T>(
        this IncomingRequest request,
        DecodeFunc<T> decodeFunc,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return request.DecodeValueAsync(
            feature,
            feature.BaseProxy,
            decodeFunc,
            cancellationToken);
    }

    /// <summary>Verifies that a request payload carries no argument or only unknown tagged arguments.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that completes when the checking is complete.</returns>
    public static ValueTask DecodeEmptyArgsAsync(
        this IncomingRequest request,
        CancellationToken cancellationToken = default) =>
        request.DecodeVoidAsync(
            request.Features.Get<ISliceFeature>() ?? SliceFeature.Default,
            cancellationToken);

    /// <summary>Dispatches an incoming request to a method that matches the request's operation name.</summary>
    /// <typeparam name="TArgs">The type of the operation arguments.</typeparam>
    /// <typeparam name="TReturnValue">The type of the operation return value.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeArgs">A function that decodes the arguments from the request payload.</param>
    /// <param name="method">The user-provided implementation of the operation.</param>
    /// <param name="encodeReturnValue">A function that encodes the return value into a PipeReader.</param>
    /// <param name="encodeReturnValueStream">A function that encodes the stream portion of the return value.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchOperationAsync<TArgs, TReturnValue>(
        this IncomingRequest request,
        Func<IncomingRequest, CancellationToken, ValueTask<TArgs>> decodeArgs,
        Func<TArgs, IFeatureCollection, CancellationToken, ValueTask<TReturnValue>> method,
        Func<TReturnValue, SliceEncodeOptions?, PipeReader> encodeReturnValue,
        Func<TReturnValue, SliceEncodeOptions?, PipeReader>? encodeReturnValueStream = null,
        CancellationToken cancellationToken = default)
    {
        TArgs args = await decodeArgs(request, cancellationToken).ConfigureAwait(false);
        TReturnValue returnValue = await method(args, request.Features, cancellationToken).ConfigureAwait(false);

        return new OutgoingResponse(request)
        {
            Payload = encodeReturnValue(returnValue, request.Features.Get<ISliceFeature>()?.EncodeOptions),
            PayloadContinuation =
                encodeReturnValueStream?.Invoke(returnValue, request.Features.Get<ISliceFeature>()?.EncodeOptions)
        };
    }

    /// <summary>Dispatches an incoming request to a method that matches the request's operation name. The operation
    /// does not accept any arguments.</summary>
    /// <typeparam name="TReturnValue">The type of the operation return value.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeArgs">A function that decodes the empty arguments from the request payload.</param>
    /// <param name="method">The user-provided implementation of the operation.</param>
    /// <param name="encodeReturnValue">A function that encodes the return value into a PipeReader.</param>
    /// <param name="encodeReturnValueStream">A function that encodes the stream portion of the return value.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchOperationAsync<TReturnValue>(
        this IncomingRequest request,
        Func<IncomingRequest, CancellationToken, ValueTask> decodeArgs,
        Func<IFeatureCollection, CancellationToken, ValueTask<TReturnValue>> method,
        Func<TReturnValue, SliceEncodeOptions?, PipeReader> encodeReturnValue,
        Func<TReturnValue, SliceEncodeOptions?, PipeReader>? encodeReturnValueStream = null,
        CancellationToken cancellationToken = default)
    {
        await decodeArgs(request, cancellationToken).ConfigureAwait(false);
        TReturnValue returnValue = await method(request.Features, cancellationToken).ConfigureAwait(false);
        return new OutgoingResponse(request)
        {
            Payload = encodeReturnValue(returnValue, request.Features.Get<ISliceFeature>()?.EncodeOptions),
            PayloadContinuation = encodeReturnValueStream?.Invoke(returnValue, request.Features.Get<ISliceFeature>()?.EncodeOptions)
        };
    }

    /// <summary>Dispatches an incoming request to a method that matches the request's operation name. The operation
    /// does not return anything.</summary>
    /// <typeparam name="TArgs">The type of the operation arguments.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeArgs">A function that decodes the arguments from the request payload.</param>
    /// <param name="method">The user-provided implementation of the operation.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchOperationAsync<TArgs>(
        this IncomingRequest request,
        Func<IncomingRequest, CancellationToken, ValueTask<TArgs>> decodeArgs,
        Func<TArgs, IFeatureCollection, CancellationToken, ValueTask> method,
        CancellationToken cancellationToken = default)
    {
        TArgs args = await decodeArgs(request, cancellationToken).ConfigureAwait(false);
        await method(args, request.Features, cancellationToken).ConfigureAwait(false);
        return new OutgoingResponse(request);
    }

    /// <summary>Dispatches an incoming request to a method that matches the request's operation name. The operation
    /// does not accept any arguments and does not return anything.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeArgs">A function that decodes the empty arguments from the request payload.</param>
    /// <param name="method">The user-provided implementation of the operation.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchOperationAsync(
        this IncomingRequest request,
        Func<IncomingRequest, CancellationToken, ValueTask> decodeArgs,
        Func<IFeatureCollection, CancellationToken, ValueTask> method,
        CancellationToken cancellationToken = default)
    {
        await decodeArgs(request, cancellationToken).ConfigureAwait(false);
        await method(request.Features, cancellationToken).ConfigureAwait(false);
        return new OutgoingResponse(request);
    }
}
