// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Extension methods to decode the payloads of incoming responses when such payloads are encoded with the
/// Slice encoding.</summary>
public static class IncomingResponseExtensions
{
    /// <summary>Decodes a response with a <see cref="ResultType.Failure"/> result type.</summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="sender">The invoker of the proxy that sent the request.</param>
    /// <param name="encodeOptions">The encode options of the proxy that sent the request.</param>
    /// <param name="defaultActivator">The default activator.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The decoded failure.</returns>
    public static ValueTask<RemoteException> DecodeFailureAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        IInvoker sender,
        SliceEncodeOptions? encodeOptions = null,
        IActivator? defaultActivator = null,
        CancellationToken cancel = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return response.ResultType == ResultType.Failure ?
            response.DecodeRemoteExceptionAsync(
                request,
                response.Protocol.SliceEncoding,
                feature,
                defaultActivator,
                feature.ServiceProxyFactory is null ?
                    CreateServiceProxyFactory(response, feature, sender, encodeOptions) : null,
                cancel) :
            throw new ArgumentException(
                $"{nameof(DecodeFailureAsync)} requires a response with a Failure result type",
                nameof(response));
    }

    /// <summary>Decodes a response payload.</summary>
    /// <typeparam name="T">The type of the return value.</typeparam>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="encoding">The encoding of the response payload.</param>
    /// <param name="sender">The invoker of the proxy that sent the request.</param>
    /// <param name="encodeOptions">The encode options of the proxy that sent the request.</param>
    /// <param name="defaultActivator">The optional default activator.</param>
    /// <param name="decodeFunc">The decode function for the return value.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The return value.</returns>
    public static ValueTask<T> DecodeReturnValueAsync<T>(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        IInvoker sender,
        SliceEncodeOptions? encodeOptions,
        IActivator? defaultActivator,
        DecodeFunc<T> decodeFunc,
        CancellationToken cancel = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;
        Func<ServiceAddress, ServiceProxy>? defaultServiceProxyFactory = feature.ServiceProxyFactory is null ?
            CreateServiceProxyFactory(response, feature, sender, encodeOptions) : null;

        return response.ResultType == ResultType.Success ?
            response.DecodeValueAsync(
                encoding,
                feature,
                defaultActivator,
                defaultServiceProxyFactory,
                decodeFunc,
                cancel) :
            ThrowRemoteExceptionAsync();

        async ValueTask<T> ThrowRemoteExceptionAsync()
        {
            throw await response.DecodeRemoteExceptionAsync(
                request,
                encoding,
                feature,
                defaultActivator,
                defaultServiceProxyFactory,
                cancel).ConfigureAwait(false);
        }
    }

    /// <summary>Creates an async enumerable over the payload reader of an incoming response to decode fixed size
    /// streamed elements.</summary>
    /// <typeparam name="T">The stream element type.</typeparam>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="encoding">The encoding of the response payload.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <param name="elementSize">The size in bytes of the streamed elements.</param>
    /// <returns>The async enumerable to decode and return the streamed members.</returns>
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        DecodeFunc<T> decodeFunc,
        int elementSize) =>
        response.ToAsyncEnumerable(
            encoding,
            request.Features.Get<ISliceFeature>() ?? SliceFeature.Default,
            decodeFunc,
            elementSize);

    /// <summary>Creates an async enumerable over the payload reader of an incoming response to decode variable
    /// size streamed elements.</summary>
    /// <typeparam name="T">The stream element type.</typeparam>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="encoding">The encoding of the response payload.</param>
    /// <param name="sender">The invoker of the proxy that sent the request.</param>
    /// <param name="encodeOptions">The encode options of the proxy that sent the request.</param>
    /// <param name="defaultActivator">The optional default activator.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <returns>The async enumerable to decode and return the streamed members.</returns>
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        IInvoker sender,
        SliceEncodeOptions? encodeOptions,
        IActivator? defaultActivator,
        DecodeFunc<T> decodeFunc)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return response.ToAsyncEnumerable(
            encoding,
            feature,
            defaultActivator,
            feature.ServiceProxyFactory is null ?
                CreateServiceProxyFactory(response, feature, sender, encodeOptions) : null,
            decodeFunc);
    }

    /// <summary>Verifies that a response payload carries no return value or only tagged return values.</summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="encoding">The encoding of the response payload.</param>
    /// <param name="sender">The invoker of the proxy that sent the request.</param>
    /// <param name="encodeOptions">The encode options of the proxy that sent the request.</param>
    /// <param name="defaultActivator">The optional default activator.</param>
    /// <param name="cancel">The cancellation token.</param>
    public static ValueTask DecodeVoidReturnValueAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        IInvoker sender,
        SliceEncodeOptions? encodeOptions,
        IActivator? defaultActivator = null,
        CancellationToken cancel = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return response.ResultType == ResultType.Success ?
            response.DecodeVoidAsync(encoding, feature, cancel) :
            ThrowRemoteExceptionAsync();

        async ValueTask ThrowRemoteExceptionAsync()
        {
            throw await response.DecodeRemoteExceptionAsync(
                request,
                encoding,
                feature,
                defaultActivator,
                feature.ServiceProxyFactory is null ?
                    CreateServiceProxyFactory(response, feature, sender, encodeOptions) : null,
                cancel).ConfigureAwait(false);
        }
    }

    private static async ValueTask<RemoteException> DecodeRemoteExceptionAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        SliceEncoding encoding,
        ISliceFeature feature,
        IActivator? defaultActivator,
        Func<ServiceAddress, ServiceProxy>? defaultServiceProxyFactory,
        CancellationToken cancel)
    {
        Debug.Assert(response.ResultType != ResultType.Success);
        if (response.ResultType == ResultType.Failure)
        {
            encoding = response.Protocol.SliceEncoding;
        }

        var resultType = (SliceResultType)response.ResultType;
        if (resultType is SliceResultType.Failure or SliceResultType.ServiceFailure)
        {
            ReadResult readResult = await response.Payload.ReadSegmentAsync(
                encoding,
                feature.MaxSegmentSize,
                cancel).ConfigureAwait(false);

            readResult.ThrowIfCanceled(response.Protocol);

            if (readResult.Buffer.IsEmpty)
            {
                throw new InvalidDataException("empty remote exception");
            }

            RemoteException result = Decode(readResult.Buffer);
            result.Origin = request;
            response.Payload.AdvanceTo(readResult.Buffer.End);
            return result;
        }
        else
        {
            throw new InvalidDataException($"received response with invalid result type value '{resultType}'");
        }

        RemoteException Decode(ReadOnlySequence<byte> buffer)
        {
            var decoder = new SliceDecoder(
                buffer,
                encoding,
                activator: feature.Activator ?? defaultActivator,
                feature.ServiceProxyFactory ?? defaultServiceProxyFactory,
                maxCollectionAllocation: feature.MaxCollectionAllocation,
                maxDepth: feature.MaxDepth);

            RemoteException remoteException = encoding == SliceEncoding.Slice1 ?
                (resultType == SliceResultType.Failure ?
                    decoder.DecodeSystemException() :
                    decoder.DecodeUserException()) :
                decoder.DecodeTrait(CreateUnknownException);

            if (remoteException is not UnknownException)
            {
                decoder.CheckEndOfBuffer(skipTaggedParams: false);
            }
            // else, we did not decode the full exception from the buffer

            return remoteException;

            // If we can't decode this exception, we return an UnknownException with the undecodable exception's
            // type identifier and message.
            static RemoteException CreateUnknownException(string typeId, ref SliceDecoder decoder) =>
                new UnknownException(typeId, decoder.DecodeString());
        }
    }

    private static Func<ServiceAddress, ServiceProxy> CreateServiceProxyFactory(
        IncomingResponse response,
        ISliceFeature feature,
        IInvoker sender,
        SliceEncodeOptions? encodeOptions)
    {
        return CreateServiceProxy;

        ServiceProxy CreateServiceProxy(ServiceAddress serviceAddress) => serviceAddress.Protocol is null ?
            // relative service address
            new ServiceProxy
            {
                EncodeOptions = feature.EncodeOptions ?? encodeOptions,
                Invoker = response.ConnectionContext.Invoker,
                ServiceAddress = new(response.ConnectionContext.Protocol) { Path = serviceAddress.Path },
            }
            :
            new ServiceProxy
            {
                EncodeOptions = feature.EncodeOptions ?? encodeOptions,
                Invoker = sender,
                ServiceAddress = serviceAddress
            };
    }
}
