// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>A function that decodes the return value from a Slice-encoded response.</summary>
/// <typeparam name="T">The type of the return value to read.</typeparam>
/// <param name="response">The incoming response.</param>
/// <param name="request">The outgoing request.</param>
/// <param name="sender">The proxy that sent the request.</param>
/// <param name="cancellationToken">The cancellation token.</param>
/// <returns>A value task that contains the return value or a <see cref="SliceException" /> when the status code of the
/// response is <see cref="StatusCode.ApplicationError" />.</returns>
public delegate ValueTask<T> ResponseDecodeFunc<T>(
    IncomingResponse response,
    OutgoingRequest request,
    ServiceProxy sender,
    CancellationToken cancellationToken);

/// <summary>A function that decodes the "void" return value from a Slice-encoded response.</summary>
/// <param name="response">The incoming response.</param>
/// <param name="request">The outgoing request.</param>
/// <param name="sender">The proxy that sent the request.</param>
/// <param name="cancellationToken">The cancellation token.</param>
/// <returns>A value task that contains a <see cref="SliceException" /> when the status code of the response is
/// <see cref="StatusCode.ApplicationError" />.</returns>
public delegate ValueTask ResponseDecodeFunc(
    IncomingResponse response,
    OutgoingRequest request,
    ServiceProxy sender,
    CancellationToken cancellationToken);

/// <summary>Provides extension methods for interface <see cref="IProxy" /> and generated proxy structs that implement
/// this interface.</summary>
public static class ProxyExtensions
{
    private static readonly IDictionary<RequestFieldKey, OutgoingFieldValue> _idempotentFields =
        new Dictionary<RequestFieldKey, OutgoingFieldValue>
        {
            [RequestFieldKey.Idempotent] = default
        }.ToImmutableDictionary();

    /// <summary>Tests whether the target service implements the interface implemented by the TProxy proxy. This
    /// method is a wrapper for <see cref="IServiceProxy.IceIsAAsync" />.</summary>
    /// <typeparam name="TProxy">The type of the target proxy struct.</typeparam>
    /// <param name="proxy">The source Proxy being tested.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A new TProxy instance, or null.</returns>
    public static async Task<TProxy?> AsAsync<TProxy>(
        this IProxy proxy,
        IFeatureCollection? features = null,
        CancellationToken cancellationToken = default) where TProxy : struct, IProxy =>
        await proxy.ToProxy<ServiceProxy>().IceIsAAsync(typeof(TProxy).GetSliceTypeId()!, features, cancellationToken)
            .ConfigureAwait(false) ?
            proxy.ToProxy<TProxy>() : null;

    /// <summary>Sends a request to a service and decodes the response.</summary>
    /// <typeparam name="TProxy">The type of the proxy struct.</typeparam>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="proxy">A proxy to the remote service.</param>
    /// <param name="operation">The name of the operation, as specified in Slice.</param>
    /// <param name="payload">The payload of the request. <c>null</c> is equivalent to an empty payload.</param>
    /// <param name="payloadContinuation">The optional payload continuation of the request.</param>
    /// <param name="responseDecodeFunc">The decode function for the response payload. It decodes and throws an
    /// exception when the status code of the response is <see cref="StatusCode.ApplicationError" />.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <see langword="true" />, the request is idempotent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation's return value.</returns>
    /// <exception cref="SliceException">Thrown if the response carries a Slice exception.</exception>
    /// <remarks>This method stores the response features into the invocation's response features when invocation is not
    /// null.</remarks>
    public static Task<T> InvokeAsync<TProxy, T>(
        this TProxy proxy,
        string operation,
        PipeReader? payload,
        PipeReader? payloadContinuation,
        ResponseDecodeFunc<T> responseDecodeFunc,
        IFeatureCollection? features,
        bool idempotent = false,
        CancellationToken cancellationToken = default) where TProxy : struct, IProxy
    {
        if (proxy.Invoker is not IInvoker invoker)
        {
            throw new InvalidOperationException("a proxy with a null invoker cannot send requests");
        }

        if (payload is null && payloadContinuation is not null)
        {
            throw new ArgumentNullException(
                nameof(payload),
                $"when {nameof(payloadContinuation)} is not null, {nameof(payload)} cannot be null");
        }

        var request = new OutgoingRequest(proxy.ServiceAddress)
        {
            Features = features ?? FeatureCollection.Empty,
            Fields = idempotent ?
                _idempotentFields : ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty,
            Operation = operation,
            Payload = payload ?? EmptyPipeReader.Instance,
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
                return await responseDecodeFunc(
                    response,
                    request,
                    new ServiceProxy(invoker, proxy.ServiceAddress, proxy.EncodeOptions),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                request.Dispose();
            }
        }
    }

    /// <summary>Sends a request to a service and decodes the "void" response.</summary>
    /// <typeparam name="TProxy">The type of the proxy struct.</typeparam>
    /// <param name="proxy">A proxy for the remote service.</param>
    /// <param name="operation">The name of the operation, as specified in Slice.</param>
    /// <param name="payload">The payload of the request. <c>null</c> is equivalent to an empty payload.</param>
    /// <param name="payloadContinuation">The payload continuation of the request.</param>
    /// <param name="responseDecodeFunc">The decode function for the response payload. It decodes and throws an
    /// exception when the status code of the response is <see cref="StatusCode.ApplicationError" />.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <see langword="true" />, the request is idempotent.</param>
    /// <param name="oneway">When <see langword="true" />, the request is sent oneway and an empty response is returned
    /// immediately after sending the request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the void response is returned.</returns>
    /// <exception cref="SliceException">Thrown if the response carries a failure.</exception>
    /// <remarks>This method stores the response features into the invocation's response features when invocation is
    /// not null.</remarks>
    public static Task InvokeAsync<TProxy>(
        this TProxy proxy,
        string operation,
        PipeReader? payload,
        PipeReader? payloadContinuation,
        ResponseDecodeFunc responseDecodeFunc,
        IFeatureCollection? features,
        bool idempotent = false,
        bool oneway = false,
        CancellationToken cancellationToken = default) where TProxy : struct, IProxy
    {
        if (proxy.Invoker is not IInvoker invoker)
        {
            throw new InvalidOperationException("a proxy with a null invoker cannot send requests");
        }

        if (payload is null && payloadContinuation is not null)
        {
            throw new ArgumentNullException(
                nameof(payload),
                $"when {nameof(payloadContinuation)} is not null, {nameof(payload)} cannot be null");
        }

        var request = new OutgoingRequest(proxy.ServiceAddress)
        {
            Features = features ?? FeatureCollection.Empty,
            Fields = idempotent ?
                _idempotentFields : ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty,
            IsOneway = oneway,
            Operation = operation,
            Payload = payload ?? EmptyPipeReader.Instance,
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

        async Task ReadResponseAsync(Task<IncomingResponse> responseTask, OutgoingRequest request)
        {
            try
            {
                IncomingResponse response = await responseTask.ConfigureAwait(false);

                await responseDecodeFunc(
                    response,
                    request,
                    new ServiceProxy(invoker, proxy.ServiceAddress, proxy.EncodeOptions),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                request.Dispose();
            }
        }
    }

    /// <summary>Converts a proxy struct into another proxy struct. This convertion always succeeds.</summary>
    /// <typeparam name="TProxy">The type of the target proxy struct.</typeparam>
    /// <param name="proxy">The source Proxy.</param>
    /// <returns>A new TProxy instance.</returns>
    public static TProxy ToProxy<TProxy>(this IProxy proxy) where TProxy : struct, IProxy =>
        new() { EncodeOptions = proxy.EncodeOptions, Invoker = proxy.Invoker, ServiceAddress = proxy.ServiceAddress };
}
