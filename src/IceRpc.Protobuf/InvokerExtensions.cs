// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using IceRpc.Features;
using System.Collections.Immutable;

namespace IceRpc.Protobuf;

/// <summary>Represents a delegate that decodes the return value from a Protobuf-encoded response.</summary>
/// <typeparam name="T">The type of the return value to read.</typeparam>
/// <param name="response">The incoming response.</param>
/// <param name="request">The outgoing request.</param>
/// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
/// <returns>A value task that contains the return value.</returns>
public delegate ValueTask<T> ResponseDecodeFunc<T>(
    IncomingResponse response,
    OutgoingRequest request,
    CancellationToken cancellationToken);

/// <summary>Provides extension methods for <see cref="IInvoker" />.</summary>
public static class InvokerExtensions
{
    private static readonly IDictionary<RequestFieldKey, OutgoingFieldValue> _idempotentFields =
        new Dictionary<RequestFieldKey, OutgoingFieldValue>
        {
            [RequestFieldKey.Idempotent] = default
        }.ToImmutableDictionary();

    /// <summary>Sends a request to a service and decodes the response.</summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="invoker">The invoker used to send the request.</param>
    /// <param name="serviceAddress">The address of the target service.</param>
    /// <param name="operation">The name of the operation, as specified in Protobuf.</param>
    /// <param name="inputMessage">The input message to encode in the request payload.</param>
    /// <param name="encodeOptions">The options to customize the encoding of the request payload.</param>
    /// <param name="responseDecodeFunc">The <see cref="ResponseDecodeFunc{T}"/> used to decode the response payload.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <see langword="true" />, the request is idempotent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The operation's return value.</returns>
    public static Task<T> InvokeAsync<T>(
        this IInvoker invoker,
        ServiceAddress serviceAddress,
        string operation,
        IMessage inputMessage,
        ProtobufEncodeOptions? encodeOptions,
        ResponseDecodeFunc<T> responseDecodeFunc,
        IFeatureCollection? features = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default)
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

        // ReadResponseAsync is responsible for disposing the request
        return ReadResponseAsync(responseTask, request);

        async Task<T> ReadResponseAsync(Task<IncomingResponse> responseTask, OutgoingRequest request)
        {
            try
            {
                IncomingResponse response = await responseTask.ConfigureAwait(false);

                if (response.StatusCode == StatusCode.Ok)
                {
                    var protobufFeature = request.Features.Get<IProtobufFeature>() ?? ProtobufFeature.Default;
                    return await responseDecodeFunc(response, request, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // IceRPC guarantees the error message is non-null when StatusCode > Ok.
                    throw new DispatchException(response.StatusCode, response.ErrorMessage!);
                }
            }
            finally
            {
                request.Dispose();
            }
        }
    }

    /// <summary>Sends a request to a service and decodes the response.</summary>
    /// <typeparam name="TOutputParam">The response type.</typeparam>
    /// <typeparam name="TInputParam">The input type.</typeparam>
    /// <param name="invoker">The invoker used to send the request.</param>
    /// <param name="serviceAddress">The address of the target service.</param>
    /// <param name="operation">The name of the operation, as specified in Protobuf.</param>
    /// <param name="stream">The stream of input message to encode in the request payload continuation.</param>
    /// <param name="encodeOptions">The options to customize the encoding of the request payload continuation.</param>
    /// <param name="responseDecodeFunc">The <see cref="ResponseDecodeFunc{T}"/> used to decode the response payload.
    /// </param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <see langword="true" />, the request is idempotent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The operation's return value.</returns>
    public static Task<TOutputParam> InvokeAsync<TOutputParam, TInputParam>(
        this IInvoker invoker,
        ServiceAddress serviceAddress,
        string operation,
        IAsyncEnumerable<TInputParam> stream,
        ProtobufEncodeOptions? encodeOptions,
        ResponseDecodeFunc<TOutputParam> responseDecodeFunc,
        IFeatureCollection? features = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default)
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

        // ReadResponseAsync is responsible for disposing the request
        return ReadResponseAsync(responseTask, request);

        async Task<TOutputParam> ReadResponseAsync(Task<IncomingResponse> responseTask, OutgoingRequest request)
        {
            try
            {
                IncomingResponse response = await responseTask.ConfigureAwait(false);

                if (response.StatusCode == StatusCode.Ok)
                {
                    return await responseDecodeFunc(response, request, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // IceRPC guarantees the error message is non-null when StatusCode > Ok.
                    throw new DispatchException(response.StatusCode, response.ErrorMessage!);
                }
            }
            finally
            {
                request.Dispose();
            }
        }
    }
}
