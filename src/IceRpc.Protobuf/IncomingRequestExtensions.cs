// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using IceRpc.Features;

namespace IceRpc.Protobuf;

/// <summary>Provides extension methods for <see cref="IncomingRequest" />.</summary>
public static class IncomingRequestExtensions
{
    /// <summary>Dispatches a unary RPC method.</summary>
    /// <typeparam name="TInput">The type of the input message.</typeparam>
    /// <typeparam name="TOutput">The type of the output message.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="inputParser">A message parser used to decode the request payload.</param>
    /// <param name="method">The user-provided implementation of the RPC method.</param>
    /// <param name="cancellationToken">The cancellation token that accepts cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchUnaryAsync<TInput, TOutput>(
        this IncomingRequest request,
        MessageParser<TInput> inputParser,
        Func<TInput, IFeatureCollection, CancellationToken, ValueTask<TOutput>> method,
        CancellationToken cancellationToken) where TInput : IMessage<TInput>
                                             where TOutput : IMessage<TOutput>
    {
        IProtobufFeature protobufFeature = request.Features.Get<IProtobufFeature>() ?? ProtobufFeature.Default;

        TInput input = await request.Payload.DecodeProtobufMessageAsync(
            inputParser,
            protobufFeature.MaxMessageLength,
            cancellationToken).ConfigureAwait(false);

        TOutput output = await method(input, request.Features, cancellationToken).ConfigureAwait(false);

        return new OutgoingResponse(request)
        {
            Payload = output.EncodeAsLengthPrefixedMessage(
                protobufFeature.EncodeOptions?.PipeOptions ?? ProtobufEncodeOptions.Default.PipeOptions)
        };
    }

    /// <summary>Dispatches a client-streaming RPC method.</summary>
    /// <typeparam name="TInput">The type of the input message.</typeparam>
    /// <typeparam name="TOutput">The type of the output message.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="inputParser">A message parser used to decode the request payload.</param>
    /// <param name="method">The user-provided implementation of the RPC method.</param>
    /// <param name="cancellationToken">The cancellation token that accepts cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchClientStreamingAsync<TInput, TOutput>(
        this IncomingRequest request,
        MessageParser<TInput> inputParser,
        Func<IAsyncEnumerable<TInput>, IFeatureCollection, CancellationToken, ValueTask<TOutput>> method,
        CancellationToken cancellationToken) where TInput : IMessage<TInput>
                                             where TOutput : IMessage<TOutput>
    {
        IProtobufFeature protobufFeature = request.Features.Get<IProtobufFeature>() ?? ProtobufFeature.Default;

        IAsyncEnumerable<TInput> input = request.DetachPayload().ToAsyncEnumerable(
            inputParser,
            protobufFeature.MaxMessageLength,
            cancellationToken);

        TOutput output = await method(input, request.Features, cancellationToken).ConfigureAwait(false);

        return new OutgoingResponse(request)
        {
            Payload = output.EncodeAsLengthPrefixedMessage(
                protobufFeature.EncodeOptions?.PipeOptions ?? ProtobufEncodeOptions.Default.PipeOptions)
        };
    }

    /// <summary>Dispatches a server-streaming RPC method.</summary>
    /// <typeparam name="TInput">The type of the input message.</typeparam>
    /// <typeparam name="TOutput">The type of the output message.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="inputParser">A message parser used to decode the request payload.</param>
    /// <param name="method">The user-provided implementation of the RPC method.</param>
    /// <param name="cancellationToken">The cancellation token that accepts cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchServerStreamingAsync<TInput, TOutput>(
        this IncomingRequest request,
        MessageParser<TInput> inputParser,
        Func<TInput, IFeatureCollection, CancellationToken, ValueTask<IAsyncEnumerable<TOutput>>> method,
        CancellationToken cancellationToken) where TInput : IMessage<TInput>
                                             where TOutput : IMessage<TOutput>
    {
        IProtobufFeature protobufFeature = request.Features.Get<IProtobufFeature>() ?? ProtobufFeature.Default;

        TInput input = await request.Payload.DecodeProtobufMessageAsync(
            inputParser,
            protobufFeature.MaxMessageLength,
            cancellationToken).ConfigureAwait(false);

        IAsyncEnumerable<TOutput> output = await method(input, request.Features, cancellationToken)
            .ConfigureAwait(false);

        return new OutgoingResponse(request)
        {
            PayloadContinuation = output.ToPipeReader(protobufFeature.EncodeOptions)
        };
    }

    /// <summary>Dispatches a bidi-streaming RPC method.</summary>
    /// <typeparam name="TInput">The type of the input message.</typeparam>
    /// <typeparam name="TOutput">The type of the output message.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="inputParser">A message parser used to decode the request payload.</param>
    /// <param name="method">The user-provided implementation of the RPC method.</param>
    /// <param name="cancellationToken">The cancellation token that accepts cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchBidiStreamingAsync<TInput, TOutput>(
        this IncomingRequest request,
        MessageParser<TInput> inputParser,
        Func<IAsyncEnumerable<TInput>, IFeatureCollection, CancellationToken, ValueTask<IAsyncEnumerable<TOutput>>> method,
        CancellationToken cancellationToken) where TInput : IMessage<TInput>
                                             where TOutput : IMessage<TOutput>
    {
        IProtobufFeature protobufFeature = request.Features.Get<IProtobufFeature>() ?? ProtobufFeature.Default;

        IAsyncEnumerable<TInput> input = request.DetachPayload().ToAsyncEnumerable(
            inputParser,
            protobufFeature.MaxMessageLength,
            cancellationToken);

        IAsyncEnumerable<TOutput> output = await method(input, request.Features, cancellationToken)
            .ConfigureAwait(false);

        return new OutgoingResponse(request)
        {
            PayloadContinuation = output.ToPipeReader(protobufFeature.EncodeOptions)
        };
    }
}
