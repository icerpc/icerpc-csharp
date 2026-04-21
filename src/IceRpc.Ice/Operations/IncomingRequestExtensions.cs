// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Ice.Codec;
using IceRpc.Ice.Operations.Internal;
using System.IO.Pipelines;

namespace IceRpc.Ice.Operations;

/// <summary>Provides extension methods for <see cref="IncomingRequest" /> to decode its Ice-encoded payload.
/// </summary>
public static class IncomingRequestExtensions
{
    /// <summary>Creates an outgoing response with status code <see cref="StatusCode.ApplicationError" /> with an Ice
    /// exception payload.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="iceException">The Ice exception to encode in the payload.</param>
    /// <returns>The new outgoing response.</returns>
    public static OutgoingResponse CreateIceExceptionResponse(
        this IncomingRequest request,
        IceException iceException)
    {
        IceEncodeOptions encodeOptions =
            request.Features.Get<IIceFeature>()?.EncodeOptions ?? IceEncodeOptions.Default;

        var pipe = new Pipe(encodeOptions.PipeOptions);

        try
        {
            var encoder = new IceEncoder(pipe.Writer);
            iceException.Encode(ref encoder);
            pipe.Writer.Complete();

            // The message is always the default C# message since the generated Ice exceptions don't provide a way to
            // set the message.
            // This default message is only transmitted over icerpc; the icerpc client uses this message when it can't
            // decode the Ice exception. See IceDecoder.DecodeException for more details.
            return new OutgoingResponse(request, StatusCode.ApplicationError, iceException.Message)
            {
                Payload = pipe.Reader
            };
        }
        catch
        {
            pipe.Reader.Complete();
            pipe.Writer.Complete();
            throw;
        }
    }

    /// <summary>Decodes a request payload into a list of arguments.</summary>
    /// <typeparam name="T">The type of the request parameters.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
    /// <param name="defaultActivator">The activator to use when the activator provided by the request's
    /// <see cref="IIceFeature" /> is <see langword="null" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The request arguments.</returns>
    public static ValueTask<T> DecodeArgsAsync<T>(
        this IncomingRequest request,
        DecodeFunc<T> decodeFunc,
        IActivator defaultActivator,
        CancellationToken cancellationToken = default)
    {
        IIceFeature feature = request.Features.Get<IIceFeature>() ?? IceFeature.Default;

        return request.DecodeValueAsync(
            feature,
            feature.BaseProxy,
            decodeFunc,
            feature.Activator ?? defaultActivator,
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
            request.Features.Get<IIceFeature>() ?? IceFeature.Default,
            cancellationToken);

    /// <summary>Dispatches an incoming request to a method that matches the request's operation name.</summary>
    /// <typeparam name="TArgs">The type of the operation arguments.</typeparam>
    /// <typeparam name="TReturnValue">The type of the operation return value.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeArgs">A function that decodes the arguments from the request payload.</param>
    /// <param name="method">The user-provided implementation of the operation.</param>
    /// <param name="encodeReturnValue">A function that encodes the return value into a PipeReader.</param>
    /// <param name="inExceptionSpecification">A function that returns <see langword="true" /> when the provided Ice
    /// exception conforms to the exception specification; otherwise, <see langword="false" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchOperationAsync<TArgs, TReturnValue>(
        this IncomingRequest request,
        Func<IncomingRequest, CancellationToken, ValueTask<TArgs>> decodeArgs,
        Func<TArgs, IFeatureCollection, CancellationToken, ValueTask<TReturnValue>> method,
        Func<TReturnValue, IceEncodeOptions?, PipeReader> encodeReturnValue,
        Func<IceException, bool>? inExceptionSpecification = null,
        CancellationToken cancellationToken = default)
    {
        TArgs args = await decodeArgs(request, cancellationToken).ConfigureAwait(false);
        try
        {
            TReturnValue returnValue = await method(args, request.Features, cancellationToken).ConfigureAwait(false);
            return new OutgoingResponse(request)
            {
                Payload = encodeReturnValue(returnValue, request.Features.Get<IIceFeature>()?.EncodeOptions),
            };
        }
        catch (IceException iceException) when (inExceptionSpecification?.Invoke(iceException) ?? false)
        {
            return request.CreateIceExceptionResponse(iceException);
        }
    }

    /// <summary>Dispatches an incoming request to a method that matches the request's operation name. The operation
    /// does not accept any arguments.</summary>
    /// <typeparam name="TReturnValue">The type of the operation return value.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeArgs">A function that decodes the empty arguments from the request payload.</param>
    /// <param name="method">The user-provided implementation of the operation.</param>
    /// <param name="encodeReturnValue">A function that encodes the return value into a PipeReader.</param>
    /// <param name="inExceptionSpecification">A function that returns <see langword="true" /> when the provided Ice
    /// exception conforms to the exception specification; otherwise, <see langword="false" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchOperationAsync<TReturnValue>(
        this IncomingRequest request,
        Func<IncomingRequest, CancellationToken, ValueTask> decodeArgs,
        Func<IFeatureCollection, CancellationToken, ValueTask<TReturnValue>> method,
        Func<TReturnValue, IceEncodeOptions?, PipeReader> encodeReturnValue,
        Func<IceException, bool>? inExceptionSpecification = null,
        CancellationToken cancellationToken = default)
    {
        await decodeArgs(request, cancellationToken).ConfigureAwait(false);
        try
        {
            TReturnValue returnValue = await method(request.Features, cancellationToken).ConfigureAwait(false);
            return new OutgoingResponse(request)
            {
                Payload = encodeReturnValue(returnValue, request.Features.Get<IIceFeature>()?.EncodeOptions)
            };
        }
        catch (IceException iceException) when (inExceptionSpecification?.Invoke(iceException) ?? false)
        {
            return request.CreateIceExceptionResponse(iceException);
        }
    }

    /// <summary>Dispatches an incoming request to a method that matches the request's operation name. The operation
    /// does not return anything.</summary>
    /// <typeparam name="TArgs">The type of the operation arguments.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeArgs">A function that decodes the arguments from the request payload.</param>
    /// <param name="method">The user-provided implementation of the operation.</param>
    /// <param name="inExceptionSpecification">A function that returns <see langword="true" /> when the provided Ice
    /// exception conforms to the exception specification; otherwise, <see langword="false" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchOperationAsync<TArgs>(
        this IncomingRequest request,
        Func<IncomingRequest, CancellationToken, ValueTask<TArgs>> decodeArgs,
        Func<TArgs, IFeatureCollection, CancellationToken, ValueTask> method,
        Func<IceException, bool>? inExceptionSpecification = null,
        CancellationToken cancellationToken = default)
    {
        TArgs args = await decodeArgs(request, cancellationToken).ConfigureAwait(false);
        try
        {
            await method(args, request.Features, cancellationToken).ConfigureAwait(false);
            return new OutgoingResponse(request);
        }
        catch (IceException iceException) when (inExceptionSpecification?.Invoke(iceException) ?? false)
        {
            return request.CreateIceExceptionResponse(iceException);
        }
    }

    /// <summary>Dispatches an incoming request to a method that matches the request's operation name. The operation
    /// does not accept any arguments and does not return anything.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeArgs">A function that decodes the empty arguments from the request payload.</param>
    /// <param name="method">The user-provided implementation of the operation.</param>
    /// <param name="inExceptionSpecification">A function that returns <see langword="true" /> when the provided Ice
    /// exception conforms to the exception specification; otherwise, <see langword="false" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchOperationAsync(
        this IncomingRequest request,
        Func<IncomingRequest, CancellationToken, ValueTask> decodeArgs,
        Func<IFeatureCollection, CancellationToken, ValueTask> method,
        Func<IceException, bool>? inExceptionSpecification = null,
        CancellationToken cancellationToken = default)
    {
        await decodeArgs(request, cancellationToken).ConfigureAwait(false);
        try
        {
            await method(request.Features, cancellationToken).ConfigureAwait(false);
            return new OutgoingResponse(request);
        }
        catch (IceException iceException) when (inExceptionSpecification?.Invoke(iceException) ?? false)
        {
            return request.CreateIceExceptionResponse(iceException);
        }
    }
}
