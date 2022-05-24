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
/// <param name="encodeFeature">The encode feature of the Prx struct that sent the request.</param>
/// <param name="cancel">The cancellation token.</param>
/// <returns>A value task that contains the return value or a <see cref="RemoteException"/> when the response
/// carries a failure.</returns>
public delegate ValueTask<T> ResponseDecodeFunc<T>(
    IncomingResponse response,
    OutgoingRequest request,
    ISliceEncodeFeature? encodeFeature,
    CancellationToken cancel);

/// <summary>Provides extension methods for interface <see cref="IPrx"/> and generated Prx structs that implement this
/// interface.</summary>
public static class PrxExtensions
{
    private static readonly IDictionary<RequestFieldKey, OutgoingFieldValue> _idempotentFields =
        new Dictionary<RequestFieldKey, OutgoingFieldValue>
        {
            [RequestFieldKey.Idempotent] = default
        }.ToImmutableDictionary();

    /// <summary>Tests whether the target service implements the interface implemented by the TPrx typed proxy. This
    /// method is a wrapper for <see cref="IServicePrx.IceIsAAsync"/>.</summary>
    /// <paramtype name="TPrx">The type of the target Prx struct.</paramtype>
    /// <param name="prx">The source Prx being tested.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A new TPrx instance, or null.</returns>
    public static async Task<TPrx?> AsAsync<TPrx>(
        this IPrx prx,
        IFeatureCollection? features = null,
        CancellationToken cancel = default) where TPrx : struct, IPrx =>
        await new ServicePrx(prx.Proxy).IceIsAAsync(typeof(TPrx).GetSliceTypeId()!, features, cancel).
            ConfigureAwait(false) ?
            new TPrx { EncodeFeature = prx.EncodeFeature, Proxy = prx.Proxy } : null;

    /// <summary>Sends a request to a service and decodes the response.</summary>
    /// <param name="prx">A proxy to the remote service.</param>
    /// <param name="operation">The name of the operation, as specified in Slice.</param>
    /// <param name="payload">The payload of the request. <c>null</c> is equivalent to an empty payload.</param>
    /// <param name="payloadStream">The optional payload stream of the request.</param>
    /// <param name="responseDecodeFunc">The decode function for the response payload. It decodes and throws a
    /// <see cref="RemoteException"/> when the response payload contains a failure.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When <c>true</c>, the request is idempotent.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The operation's return value.</returns>
    /// <exception cref="RemoteException">Thrown if the response carries a failure.</exception>
    /// <remarks>This method stores the response features into the invocation's response features when
    /// invocation is not null.</remarks>
    public static Task<T> InvokeAsync<TPrx, T>(
        this TPrx prx,
        string operation,
        PipeReader? payload,
        PipeReader? payloadStream,
        ResponseDecodeFunc<T> responseDecodeFunc,
        IFeatureCollection? features,
        bool idempotent = false,
        CancellationToken cancel = default) where TPrx : struct, IPrx
    {
        if (payload == null && payloadStream != null)
        {
            throw new ArgumentNullException(
                nameof(payload),
                $"when {nameof(payloadStream)} is not null, {nameof(payload)} cannot be null");
        }

        var request = new OutgoingRequest(prx.Proxy)
        {
            Features = features ?? FeatureCollection.Empty,
            Fields = idempotent ?
                _idempotentFields : ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty,
            Operation = operation,
            Payload = payload ?? EmptyPipeReader.Instance,
            PayloadStream = payloadStream
        };

        IInvoker invoker = prx.Proxy.Invoker;

        try
        {
            // We perform as much work as possible in a non async method to throw exceptions synchronously.
            return ReadResponseAsync(invoker.InvokeAsync(request, cancel), request);
        }
        catch (Exception exception) // synchronous exception throws by InvokeAsync
        {
            request.Complete(exception);
            throw;
        }
        // if the call succeeds, ReadResponseAsync is responsible for completing the request

        async Task<T> ReadResponseAsync(Task<IncomingResponse> responseTask, OutgoingRequest request)
        {
            Exception? exception = null;
            try
            {
                IncomingResponse response = await responseTask.ConfigureAwait(false);
                return await responseDecodeFunc(response, request, prx.EncodeFeature, cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                request.Complete(exception);
            }
        }
    }

    /// <summary>Sends a request to a service and decodes the "void" response.</summary>
    /// <param name="prx">A proxy for the remote service.</param>
    /// <param name="operation">The name of the operation, as specified in Slice.</param>
    /// <param name="encoding">The encoding of the request payload.</param>
    /// <param name="payload">The payload of the request. <c>null</c> is equivalent to an empty payload.</param>
    /// <param name="payloadStream">The payload stream of the request.</param>
    /// <param name="defaultActivator">The optional default activator.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="idempotent">When true, the request is idempotent.</param>
    /// <param name="oneway">When true, the request is sent oneway and an empty response is returned immediately
    /// after sending the request.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>A task that completes when the void response is returned.</returns>
    /// <exception cref="RemoteException">Thrown if the response carries a failure.</exception>
    /// <remarks>This method stores the response features into the invocation's response features when invocation is
    /// not null.</remarks>
    public static Task InvokeAsync<TPrx>(
        this TPrx prx,
        string operation,
        SliceEncoding encoding,
        PipeReader? payload,
        PipeReader? payloadStream,
        IActivator? defaultActivator,
        IFeatureCollection? features,
        bool idempotent = false,
        bool oneway = false,
        CancellationToken cancel = default) where TPrx : struct, IPrx
    {
        if (payload == null && payloadStream != null)
        {
            throw new ArgumentNullException(
                nameof(payload),
                $"when {nameof(payloadStream)} is not null, {nameof(payload)} cannot be null");
        }

        var request = new OutgoingRequest(prx.Proxy)
        {
            Features = features ?? FeatureCollection.Empty,
            Fields = idempotent ?
                _idempotentFields : ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty,
            IsOneway = oneway,
            Operation = operation,
            Payload = payload ?? EmptyPipeReader.Instance,
            PayloadStream = payloadStream
        };

        IInvoker invoker = prx.Proxy.Invoker;

        try
        {
            // We perform as much work as possible in a non async method to throw exceptions synchronously.
            return ReadResponseAsync(invoker.InvokeAsync(request, cancel), request);
        }
        catch (Exception exception) // synchronous exception thrown by InvokeAsync
        {
            request.Complete(exception);
            throw;
        }
        // if the call succeeds, ReadResponseAsync is responsible for completing the request

        async Task ReadResponseAsync(Task<IncomingResponse> responseTask, OutgoingRequest request)
        {
            Exception? exception = null;
            try
            {
                IncomingResponse response = await responseTask.ConfigureAwait(false);

                await response.DecodeVoidReturnValueAsync(
                    request,
                    encoding,
                    defaultActivator,
                    prx.EncodeFeature,
                    cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                request.Complete(exception);
            }
        }
    }

    /// <summary>Converts a proxy struct into another proxy struct. This convertion always succeeds.</summary>
    /// <paramtype name="TPrx">The type of the target Prx struct.</paramtype>
    /// <param name="prx">The source Prx.</param>
    /// <returns>A new TPrx instance.</returns>
    public static TPrx ToPrx<TPrx>(this IPrx prx) where TPrx : struct, IPrx =>
        new() { EncodeFeature = prx.EncodeFeature, Proxy = prx.Proxy };

    /// <summary>Converts this Prx struct into a string using a specific format.</summary>
    /// <paramtype name="TPrx">The type of source Prx struct.</paramtype>
    /// <param name="prx">The Prx struct.</param>
    /// <param name="format">The proxy format.</param>
    public static string ToString<TPrx>(this TPrx prx, IProxyFormat format) where TPrx : struct, IPrx =>
        format.ToString(prx.Proxy);
}
