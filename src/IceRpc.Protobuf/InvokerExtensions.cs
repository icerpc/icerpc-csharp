// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using IceRpc.Features;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc.Protobuf;

/// <summary>Provides extension methods for <see cref="IInvoker" />.</summary>
public static class InvokerExtensions
{
    private static readonly IDictionary<RequestFieldKey, OutgoingFieldValue> _idempotentFields =
        new Dictionary<RequestFieldKey, OutgoingFieldValue>
        {
            [RequestFieldKey.Idempotent] = default
        }.ToImmutableDictionary();

    /// <summary>Sends a request to a service and decodes the response. This method is for Protobuf unary RPCs.
    /// </summary>
    /// <typeparam name="TOutput">The type of the output message.</typeparam>
    /// <param name="invoker">The invoker used to send the request.</param>
    /// <param name="serviceAddress">The address of the target service.</param>
    /// <param name="operation">The name of the operation, as specified in Protobuf.</param>
    /// <param name="inputMessage">The input message to encode in the request payload.</param>
    /// <param name="messageParser">The <see cref="MessageParser{T}"/> used to decode the response payload.</param>
    /// <param name="encodeOptions">The options to customize the encoding of the request payload.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <see langword="true" />, the request is idempotent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The operation's return value.</returns>
    public static Task<TOutput> InvokeUnaryAsync<TOutput>(
        this IInvoker invoker,
        ServiceAddress serviceAddress,
        string operation,
        IMessage inputMessage,
        MessageParser<TOutput> messageParser,
        ProtobufEncodeOptions? encodeOptions = null,
        IFeatureCollection? features = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default) where TOutput : IMessage<TOutput>
    {
        var request = new OutgoingRequest(serviceAddress)
        {
            Features = features ?? FeatureCollection.Empty,
            Fields = idempotent ?
                _idempotentFields : ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty,
            Operation = operation,
            Payload = inputMessage.EncodeAsLengthPrefixedMessage(
                encodeOptions?.PipeOptions ?? ProtobufEncodeOptions.Default.PipeOptions),
        };

        Task<IncomingResponse> responseTask;
        try
        {
            responseTask = invoker.InvokeAsync(request, cancellationToken);
        }
        catch
        {
            request.Dispose();
            throw;
        }

        // ReceiveResponseAsync is responsible for disposing the request
        return ReceiveResponseAsync(messageParser, responseTask, request, cancellationToken);
    }

    /// <summary>Sends a request to a service and decodes the response. This method is for Protobuf client-streaming
    /// RPCs.</summary>
    /// <typeparam name="TInput">The type of the input message.</typeparam>
    /// <typeparam name="TOutput">The type of the output message.</typeparam>
    /// <param name="invoker">The invoker used to send the request.</param>
    /// <param name="serviceAddress">The address of the target service.</param>
    /// <param name="operation">The name of the operation, as specified in Protobuf.</param>
    /// <param name="stream">The stream of input message to encode in the request payload continuation.</param>
    /// <param name="messageParser">The <see cref="MessageParser{T}"/> used to decode the response payload.</param>
    /// <param name="encodeOptions">The options to customize the encoding of the request payload continuation.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <see langword="true" />, the request is idempotent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The operation's return value.</returns>
    public static Task<TOutput> InvokeClientStreamingAsync<TInput, TOutput>(
        this IInvoker invoker,
        ServiceAddress serviceAddress,
        string operation,
        IAsyncEnumerable<TInput> stream,
        MessageParser<TOutput> messageParser,
        ProtobufEncodeOptions? encodeOptions = null,
        IFeatureCollection? features = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default) where TInput : IMessage<TInput>
                                                       where TOutput : IMessage<TOutput>
    {
        var request = new OutgoingRequest(serviceAddress)
        {
            Features = features ?? FeatureCollection.Empty,
            Fields = idempotent ?
                _idempotentFields : ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty,
            Operation = operation,
            PayloadContinuation = stream.ToPipeReader(encodeOptions),
        };

        Task<IncomingResponse> responseTask;
        try
        {
            responseTask = invoker.InvokeAsync(request, cancellationToken);
        }
        catch
        {
            request.Dispose();
            throw;
        }

        // ReceiveResponseAsync is responsible for disposing the request
        return ReceiveResponseAsync(messageParser, responseTask, request, cancellationToken);
    }

    /// <summary>Sends a request to a service and decodes the response. This method is for Protobuf server-streaming
    /// RPCs.</summary>
    /// <typeparam name="TOutput">The type of the output message.</typeparam>
    /// <param name="invoker">The invoker used to send the request.</param>
    /// <param name="serviceAddress">The address of the target service.</param>
    /// <param name="operation">The name of the operation, as specified in Protobuf.</param>
    /// <param name="inputMessage">The input message to encode in the request payload.</param>
    /// <param name="messageParser">The <see cref="MessageParser{T}"/> used to decode the response payload.</param>
    /// <param name="encodeOptions">The options to customize the encoding of the request payload.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <see langword="true" />, the request is idempotent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The operation's return value.</returns>
    public static Task<IAsyncEnumerable<TOutput>> InvokeServerStreamingAsync<TOutput>(
        this IInvoker invoker,
        ServiceAddress serviceAddress,
        string operation,
        IMessage inputMessage,
        MessageParser<TOutput> messageParser,
        ProtobufEncodeOptions? encodeOptions = null,
        IFeatureCollection? features = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default) where TOutput : IMessage<TOutput>
    {
        var request = new OutgoingRequest(serviceAddress)
        {
            Features = features ?? FeatureCollection.Empty,
            Fields = idempotent ?
                _idempotentFields : ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty,
            Operation = operation,
            Payload = inputMessage.EncodeAsLengthPrefixedMessage(
                encodeOptions?.PipeOptions ?? ProtobufEncodeOptions.Default.PipeOptions),
        };

        Task<IncomingResponse> responseTask;
        try
        {
            responseTask = invoker.InvokeAsync(request, cancellationToken);
        }
        catch
        {
            request.Dispose();
            throw;
        }

        // ReceiveStreamingResponseAsync is responsible for disposing the request
        return ReceiveStreamingResponseAsync(messageParser, responseTask, request, cancellationToken);
    }

    /// <summary>Sends a request to a service and decodes the response. This method is for Protobuf bidi-streaming
    /// RPCs.</summary>
    /// <typeparam name="TInput">The input type.</typeparam>
    /// <typeparam name="TOutput">The response type.</typeparam>
    /// <param name="invoker">The invoker used to send the request.</param>
    /// <param name="serviceAddress">The address of the target service.</param>
    /// <param name="operation">The name of the operation, as specified in Protobuf.</param>
    /// <param name="stream">The stream of input message to encode in the request payload continuation.</param>
    /// <param name="messageParser">The <see cref="MessageParser{T}"/> used to decode the response payload.</param>
    /// <param name="encodeOptions">The options to customize the encoding of the request payload continuation.</param>///
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <see langword="true" />, the request is idempotent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The operation's return value.</returns>
    public static Task<IAsyncEnumerable<TOutput>> InvokeBidiStreamingAsync<TInput, TOutput>(
        this IInvoker invoker,
        ServiceAddress serviceAddress,
        string operation,
        IAsyncEnumerable<TInput> stream,
        MessageParser<TOutput> messageParser,
        ProtobufEncodeOptions? encodeOptions = null,
        IFeatureCollection? features = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default) where TInput : IMessage<TInput>
                                                       where TOutput : IMessage<TOutput>

    {
        var request = new OutgoingRequest(serviceAddress)
        {
            Features = features ?? FeatureCollection.Empty,
            Fields = idempotent ?
                _idempotentFields : ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty,
            Operation = operation,
            PayloadContinuation = stream.ToPipeReader(encodeOptions),
        };

        Task<IncomingResponse> responseTask;
        try
        {
            responseTask = invoker.InvokeAsync(request, cancellationToken);
        }
        catch
        {
            request.Dispose();
            throw;
        }

        // ReceiveStreamingResponseAsync is responsible for disposing the request
        return ReceiveStreamingResponseAsync(messageParser, responseTask, request, cancellationToken);
    }

    private static async Task<TOutput> ReceiveResponseAsync<TOutput>(
        MessageParser<TOutput> messageParser,
        Task<IncomingResponse> responseTask,
        OutgoingRequest request,
        CancellationToken cancellationToken) where TOutput : IMessage<TOutput>
    {
        try
        {
            IncomingResponse response = await responseTask.ConfigureAwait(false);
            if (response.StatusCode == StatusCode.Ok)
            {
                IProtobufFeature protobufFeature = request.Features.Get<IProtobufFeature>() ?? ProtobufFeature.Default;
                return await response.Payload.DecodeProtobufMessageAsync(
                    messageParser,
                    protobufFeature.MaxMessageLength,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // IceRPC guarantees the error message is non-null when StatusCode > Ok.
                throw new DispatchException(response.StatusCode, response.ErrorMessage!)
                {
                    ConvertToInternalError = true
                };
            }
        }
        finally
        {
            request.Dispose();
        }
    }

    private static async Task<IAsyncEnumerable<TOutput>> ReceiveStreamingResponseAsync<TOutput>(
        MessageParser<TOutput> messageParser,
        Task<IncomingResponse> responseTask,
        OutgoingRequest request,
        CancellationToken cancellationToken) where TOutput : IMessage<TOutput>
    {
        try
        {
            IncomingResponse response = await responseTask.ConfigureAwait(false);
            if (response.StatusCode == StatusCode.Ok)
            {
                IProtobufFeature protobufFeature = request.Features.Get<IProtobufFeature>() ?? ProtobufFeature.Default;
                PipeReader payload = response.DetachPayload();
                return payload.ToAsyncEnumerable(
                    messageParser,
                    protobufFeature.MaxMessageLength,
                    cancellationToken);
            }
            else
            {
                // IceRPC guarantees the error message is non-null when StatusCode > Ok.
                throw new DispatchException(response.StatusCode, response.ErrorMessage!)
                {
                    ConvertToInternalError = true
                };
            }
        }
        finally
        {
            request.Dispose();
        }
    }
}
