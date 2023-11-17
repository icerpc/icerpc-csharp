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
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="invoker">The invoker used to send the request.</param>
    /// <param name="serviceAddress">The address of the target service.</param>
    /// <param name="operation">The name of the operation, as specified in Protobuf.</param>
    /// <param name="inputMessage">The input message to encode in the request payload.</param>
    /// <param name="encodeOptions">The options to customize the encoding of the request payload.</param>
    /// <param name="messageParser">The <see cref="MessageParser{T}"/> used to decode the response payload.
    /// </param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <see langword="true" />, the request is idempotent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The operation's return value.</returns>
    public static Task<T> InvokeUnaryAsync<T>(
        this IInvoker invoker,
        ServiceAddress serviceAddress,
        string operation,
        IMessage inputMessage,
        ProtobufEncodeOptions? encodeOptions,
        MessageParser<T> messageParser,
        IFeatureCollection? features = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default) where T : IMessage<T>
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
    /// RPCs. </summary>
    /// <typeparam name="TOutputParam">The response type.</typeparam>
    /// <typeparam name="TInputParam">The input type.</typeparam>
    /// <param name="invoker">The invoker used to send the request.</param>
    /// <param name="serviceAddress">The address of the target service.</param>
    /// <param name="operation">The name of the operation, as specified in Protobuf.</param>
    /// <param name="stream">The stream of input message to encode in the request payload continuation.</param>
    /// <param name="encodeOptions">The options to customize the encoding of the request payload continuation.</param>
    /// <param name="messageParser">The <see cref="MessageParser{T}"/> used to decode the response payload.
    /// </param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <see langword="true" />, the request is idempotent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The operation's return value.</returns>
    public static Task<TOutputParam> InvokeClientStreamingAsync<TOutputParam, TInputParam>(
        this IInvoker invoker,
        ServiceAddress serviceAddress,
        string operation,
        IAsyncEnumerable<TInputParam> stream,
        ProtobufEncodeOptions? encodeOptions,
        MessageParser<TOutputParam> messageParser,
        IFeatureCollection? features = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default)
        where TOutputParam : IMessage<TOutputParam>
        where TInputParam : IMessage<TInputParam>
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
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="invoker">The invoker used to send the request.</param>
    /// <param name="serviceAddress">The address of the target service.</param>
    /// <param name="operation">The name of the operation, as specified in Protobuf.</param>
    /// <param name="inputMessage">The input message to encode in the request payload.</param>
    /// <param name="encodeOptions">The options to customize the encoding of the request payload.</param>
    /// <param name="messageParser">The <see cref="MessageParser{T}"/> used to decode the response payload.
    /// </param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <see langword="true" />, the request is idempotent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The operation's return value.</returns>
    public static Task<IAsyncEnumerable<T>> InvokeServerStreamingAsync<T>(
        this IInvoker invoker,
        ServiceAddress serviceAddress,
        string operation,
        IMessage inputMessage,
        ProtobufEncodeOptions? encodeOptions,
        MessageParser<T> messageParser,
        IFeatureCollection? features = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default) where T : IMessage<T>
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
    /// <typeparam name="TOutputParam">The response type.</typeparam>
    /// <typeparam name="TInputParam">The input type.</typeparam>
    /// <param name="invoker">The invoker used to send the request.</param>
    /// <param name="serviceAddress">The address of the target service.</param>
    /// <param name="operation">The name of the operation, as specified in Protobuf.</param>
    /// <param name="stream">The stream of input message to encode in the request payload continuation.</param>
    /// <param name="encodeOptions">The options to customize the encoding of the request payload continuation.</param>
    /// <param name="messageParser">The <see cref="MessageParser{T}"/> used to decode the response payload.
    /// </param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <see langword="true" />, the request is idempotent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The operation's return value.</returns>
    public static Task<IAsyncEnumerable<TOutputParam>> InvokeBidiStreamingAsync<TOutputParam, TInputParam>(
        this IInvoker invoker,
        ServiceAddress serviceAddress,
        string operation,
        IAsyncEnumerable<TInputParam> stream,
        ProtobufEncodeOptions? encodeOptions,
        MessageParser<TOutputParam> messageParser,
        IFeatureCollection? features = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default)
        where TOutputParam : IMessage<TOutputParam>
        where TInputParam : IMessage<TInputParam>
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

    internal static async Task<T> ReceiveResponseAsync<T>(
        MessageParser<T> messageParser,
        Task<IncomingResponse> responseTask,
        OutgoingRequest request,
        CancellationToken cancellationToken) where T : IMessage<T>
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

    internal static async Task<IAsyncEnumerable<T>> ReceiveStreamingResponseAsync<T>(
        MessageParser<T> messageParser,
        Task<IncomingResponse> responseTask,
        OutgoingRequest request,
        CancellationToken cancellationToken) where T : IMessage<T>
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
