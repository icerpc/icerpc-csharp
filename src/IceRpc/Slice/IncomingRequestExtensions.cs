// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Extension methods to decode the payload of an incoming request when this payload is encoded with the
/// Slice encoding.</summary>
public static class IncomingRequestExtensions
{
    /// <summary>The generated code calls this method to ensure that when an operation is _not_ declared
    /// idempotent, the request is not marked idempotent. If the request is marked idempotent, it means the caller
    /// incorrectly believes this operation is idempotent.</summary>
    public static void CheckNonIdempotent(this IncomingRequest request)
    {
        if (request.Fields.ContainsKey(RequestFieldKey.Idempotent))
        {
            throw new InvalidDataException(
                $@"idempotent mismatch for operation '{request.Operation
                }': received request marked idempotent for a non-idempotent operation");
        }
    }

    /// <summary>Creates an outgoing response with a <see cref="SliceResultType.ServiceFailure"/> result type.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="remoteException">The remote exception to encode in the payload.</param>
    /// <param name="encoding">The encoding used for the request payload.</param>
    /// <returns>The new outgoing response.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="remoteException"/> is a dispatch exception or
    /// its <see cref="RemoteException.ConvertToUnhandled"/> property is <c>true</c>.</exception>
    public static OutgoingResponse CreateServiceFailureResponse(
        this IncomingRequest request,
        RemoteException remoteException,
        SliceEncoding encoding)
    {
        if (remoteException is DispatchException || remoteException.ConvertToUnhandled)
        {
            throw new ArgumentException("invalid remote exception", nameof(remoteException));
        }

        var response = new OutgoingResponse(request)
        {
            ResultType = (ResultType)SliceResultType.ServiceFailure,
            Payload = CreateExceptionPayload()
        };

        if (response.Protocol.HasFields && remoteException.RetryPolicy != RetryPolicy.NoRetry)
        {
            // Encode the retry policy into the fields of the new response.
            RetryPolicy retryPolicy = remoteException.RetryPolicy;
            response.Fields = response.Fields.With(
                ResponseFieldKey.RetryPolicy,
                (ref SliceEncoder encoder) => retryPolicy.Encode(ref encoder));
        }
        return response;

        PipeReader CreateExceptionPayload()
        {
            SliceEncodeOptions encodeOptions = request.Features.Get<ISliceFeature>()?.EncodeOptions ??
                SliceEncodeOptions.Default;

            var pipe = new Pipe(encodeOptions.PipeOptions);

            var encoder = new SliceEncoder(pipe.Writer, encoding);

            // Encode resp. EncodeTrait can throw if the exception does not support encoding.
            if (encoding == SliceEncoding.Slice1)
            {
                remoteException.Encode(ref encoder);
            }
            else
            {
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
                int startPos = encoder.EncodedByteCount;
                remoteException.EncodeTrait(ref encoder);
                SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
            }

            pipe.Writer.Complete(); // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }
    }

    /// <summary>Decodes the request's payload into a list of arguments.</summary>
    /// <typeparam name="T">The type of the request parameters.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="encoding">The encoding of the request payload.</param>
    /// <param name="defaultActivator">The activator to use when the activator of the Slice feature is null.</param>
    /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The request arguments.</returns>
    public static ValueTask<T> DecodeArgsAsync<T>(
        this IncomingRequest request,
        SliceEncoding encoding,
        IActivator? defaultActivator,
        DecodeFunc<T> decodeFunc,
        CancellationToken cancel = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return request.DecodeValueAsync(
            encoding,
            feature,
            feature.Activator ?? defaultActivator,
            feature.ServiceProxyFactory ?? CreateServiceProxyFactory(request, feature),
            decodeFunc,
            cancel);
    }

    /// <summary>Verifies that a request payload carries no argument or only unknown tagged arguments.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="encoding">The encoding of the request payload.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>A value task that completes when the checking is complete.</returns>
    public static ValueTask DecodeEmptyArgsAsync(
        this IncomingRequest request,
        SliceEncoding encoding,
        CancellationToken cancel = default) =>
        request.DecodeVoidAsync(
            encoding,
            request.Features.Get<ISliceFeature>() ?? SliceFeature.Default,
            cancel);

    /// <summary>Creates an async enumerable over the payload reader of an incoming request to decode fixed size
    /// streamed elements.</summary>
    /// <typeparam name="T">The stream element type.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="encoding">The encoding of the request payload.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <param name="elementSize">The size in bytes of the streamed elements.</param>
    /// <returns>The async enumerable to decode and return the streamed members.</returns>
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IncomingRequest request,
        SliceEncoding encoding,
        DecodeFunc<T> decodeFunc,
        int elementSize) =>
        request.ToAsyncEnumerable(
            encoding,
            request.Features.Get<ISliceFeature>() ?? SliceFeature.Default,
            decodeFunc,
            elementSize);

    /// <summary>Creates an async enumerable over the payload reader of an incoming request to decode variable size
    /// streamed elements.</summary>
    /// <typeparam name="T">The stream element type.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="encoding">The encoding of the request payload.</param>
    /// <param name="defaultActivator">The activator to use when the activator of the Slice feature is null.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <returns>The async enumerable to decode and return the streamed members.</returns>
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IncomingRequest request,
        SliceEncoding encoding,
        IActivator? defaultActivator,
        DecodeFunc<T> decodeFunc)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return request.ToAsyncEnumerable(
            encoding,
            feature,
            feature.Activator ?? defaultActivator,
            feature.ServiceProxyFactory ?? CreateServiceProxyFactory(request, feature),
            decodeFunc);
    }

    private static Func<ServiceAddress, ServiceProxy> CreateServiceProxyFactory(
        IncomingRequest request,
        ISliceFeature feature) =>
        serviceAddress => serviceAddress.Protocol is null ?
            // relative service address
            new ServiceProxy
            {
                EncodeOptions = feature.EncodeOptions,
                Invoker = request.ConnectionContext.Invoker,
                ServiceAddress = new(request.ConnectionContext.Protocol) { Path = serviceAddress.Path },
            }
            :
            new ServiceProxy
            {
                EncodeOptions = feature.EncodeOptions,
                Invoker = null,
                ServiceAddress = serviceAddress
            };
}
