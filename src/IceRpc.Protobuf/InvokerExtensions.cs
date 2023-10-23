// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using IceRpc.Features;
using System.Collections.Immutable;
using System.IO.Pipelines;

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

/// <summary>Provides an extension method for <see cref="IInvoker" />.</summary>
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
    /// <param name="payload">The payload of the request. <see langword="null" /> is equivalent to an empty
    /// payload.</param>
    /// <param name="payloadContinuation">The optional payload continuation of the request.</param>
    /// <param name="responseMessageParser">The decode function for the response payload. It decodes and throws an
    /// exception when the status code of the response is <see cref="StatusCode.ApplicationError" />.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <see langword="true" />, the request is idempotent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The operation's return value.</returns>
    public static Task<T> InvokeAsync<T>(
        this IInvoker invoker,
        ServiceAddress serviceAddress,
        string operation,
        PipeReader payload,
        PipeReader? payloadContinuation,
        MessageParser<T> responseMessageParser,
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
            Payload = payload,
            PayloadContinuation = payloadContinuation
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
                    return await response.Payload.DecodeProtobufMessageAsync(
                        responseMessageParser,
                        protobufFeature.MaxMessageLength,
                        cancellationToken).ConfigureAwait(false);
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
