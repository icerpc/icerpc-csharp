// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice.Internal;
using System.Diagnostics;
using System.IO.Pipelines;
using ZeroC.Slice;

namespace IceRpc.Slice;

/// <summary>Provides extension methods for <see cref="IncomingRequest" /> to decode its Slice-encoded payload.
/// </summary>
public static class IncomingRequestExtensions
{
    /// <summary>The generated code calls this method to ensure that when an operation is not declared idempotent,
    /// the request is not marked idempotent. If the request is marked idempotent, it means the caller incorrectly
    /// believes this operation is idempotent.</summary>
    /// <param name="request">The request to check.</param>
    /// <exception cref="InvalidDataException">Thrown if the request contains the <see cref="RequestFieldKey.Idempotent"/>
    /// field.</exception>
    public static void CheckNonIdempotent(this IncomingRequest request)
    {
        if (request.Fields.ContainsKey(RequestFieldKey.Idempotent))
        {
            throw new InvalidDataException(
                $"Invocation mode mismatch for operation '{request.Operation}': received idempotent field for an operation not marked as idempotent.");
        }
    }

    /// <summary>Creates an outgoing response with status code <see cref="StatusCode.ApplicationError" /> with
    /// a Slice exception payload.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="sliceException">The Slice exception to encode in the payload.</param>
    /// <param name="encoding">The encoding used for the request payload.</param>
    /// <returns>The new outgoing response.</returns>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="sliceException" /> does not support encoding
    /// <paramref name="encoding" />.</exception>
    public static OutgoingResponse CreateSliceExceptionResponse(
        this IncomingRequest request,
        SliceException sliceException,
        SliceEncoding encoding)
    {
        SliceEncodeOptions encodeOptions =
            request.Features.Get<ISliceFeature>()?.EncodeOptions ?? SliceEncodeOptions.Default;

        var pipe = new Pipe(encodeOptions.PipeOptions);

        try
        {
            var encoder = new SliceEncoder(pipe.Writer, encoding);

            // sliceException.Encode can throw NotSupportedException
            if (encoding == SliceEncoding.Slice1)
            {
                sliceException.Encode(ref encoder);
            }
            else
            {
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
                int startPos = encoder.EncodedByteCount;
                sliceException.Encode(ref encoder);
                SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
            }

            pipe.Writer.Complete();

            return new OutgoingResponse(request, StatusCode.ApplicationError, GetErrorMessage(sliceException))
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
    /// <param name="encoding">The encoding of the request's payload.</param>
    /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
    /// <param name="defaultActivator">The activator to use when the activator provided by the request's <see
    /// cref="ISliceFeature" /> is <see langword="null" />. Used only when <paramref name="encoding" /> is <see
    /// cref="SliceEncoding.Slice1" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The request arguments.</returns>
    public static ValueTask<T> DecodeArgsAsync<T>(
        this IncomingRequest request,
        SliceEncoding encoding,
        DecodeFunc<T> decodeFunc,
        IActivator? defaultActivator = null,
        CancellationToken cancellationToken = default)
    {
        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        return request.DecodeValueAsync(
            encoding,
            feature,
            feature.BaseProxy,
            decodeFunc,
            feature.Activator ?? defaultActivator,
            cancellationToken);
    }

    /// <summary>Verifies that a request payload carries no argument or only unknown tagged arguments.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="encoding">The encoding of the request payload.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that completes when the checking is complete.</returns>
    public static ValueTask DecodeEmptyArgsAsync(
        this IncomingRequest request,
        SliceEncoding encoding,
        CancellationToken cancellationToken = default) =>
        request.DecodeVoidAsync(
            encoding,
            request.Features.Get<ISliceFeature>() ?? SliceFeature.Default,
            cancellationToken);

    /// <summary>Dispatches an incoming request to a method that matches the request's operation name.</summary>
    /// <typeparam name="TArgs">The type of the operation arguments.</typeparam>
    /// <typeparam name="TReturnValue">The type of the operation return value.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeArgs">A function that decodes the arguments from the request payload.</param>
    /// <param name="encodeReturnValue">A function that encodes the return value into a PipeReader.</param>
    /// <param name="encodeReturnValueStream">A function that encodes the stream portion of the return value.</param>
    /// <param name="method">The user-provided implementation of the operation.</param>
    /// <param name="inExceptionSpecification">A function that returns <see langword="true" /> when the provided Slice
    /// exception conforms to the exception specification; otherwise, <see langword="false" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchOperationAsync<TArgs, TReturnValue>(
        this IncomingRequest request,
        Func<IncomingRequest, CancellationToken, ValueTask<TArgs>> decodeArgs,
        Func<TReturnValue, PipeReader> encodeReturnValue,
        Func<TReturnValue, PipeReader>? encodeReturnValueStream,
        Func<TArgs, IFeatureCollection, CancellationToken, ValueTask<TReturnValue>> method,
        Func<SliceException, bool>? inExceptionSpecification,
        CancellationToken cancellationToken)
    {
        TArgs args = await decodeArgs(request, cancellationToken).ConfigureAwait(false);
        try
        {
            TReturnValue returnValue = await method(args, request.Features, cancellationToken).ConfigureAwait(false);
            return new OutgoingResponse(request)
            {
                Payload = encodeReturnValue(returnValue),
                PayloadContinuation = encodeReturnValueStream?.Invoke(returnValue)
            };
        }
        catch (SliceException sliceException) when (inExceptionSpecification?.Invoke(sliceException) ?? false)
        {
            return request.CreateSliceExceptionResponse(sliceException, SliceEncoding.Slice1);
        }
    }

    /// <summary>Dispatches an incoming request to a method that matches the request's operation name. The operation
    /// does not accept any arguments.</summary>
    /// <typeparam name="TReturnValue">The type of the operation return value.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeEmptyArgs">A function that decodes the empty arguments from the request payload.</param>
    /// <param name="encodeReturnValue">A function that encodes the return value into a PipeReader.</param>
    /// <param name="encodeReturnValueStream">A function that encodes the stream portion of the return value.</param>
    /// <param name="method">The user-provided implementation of the operation.</param>
    /// <param name="inExceptionSpecification">A function that returns <see langword="true" /> when the provided Slice
    /// exception conforms to the exception specification; otherwise, <see langword="false" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchOperationAsync<TReturnValue>(
        this IncomingRequest request,
        Func<IncomingRequest, CancellationToken, ValueTask> decodeEmptyArgs,
        Func<TReturnValue, PipeReader> encodeReturnValue,
        Func<TReturnValue, PipeReader>? encodeReturnValueStream,
        Func<IFeatureCollection, CancellationToken, ValueTask<TReturnValue>> method,
        Func<SliceException, bool>? inExceptionSpecification,
        CancellationToken cancellationToken)
    {
        await decodeEmptyArgs(request, cancellationToken).ConfigureAwait(false);
        try
        {
            TReturnValue returnValue = await method(request.Features, cancellationToken).ConfigureAwait(false);
            return new OutgoingResponse(request)
            {
                Payload = encodeReturnValue(returnValue),
                PayloadContinuation = encodeReturnValueStream?.Invoke(returnValue)
            };
        }
        catch (SliceException sliceException) when (inExceptionSpecification?.Invoke(sliceException) ?? false)
        {
            return request.CreateSliceExceptionResponse(sliceException, SliceEncoding.Slice1);
        }
    }

    /// <summary>Dispatches an incoming request to a method that matches the request's operation name. The operation
    /// does not return anything.</summary>
    /// <typeparam name="TArgs">The type of the operation arguments.</typeparam>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeArgs">A function that decodes the arguments from the request payload.</param>
    /// <param name="method">The user-provided implementation of the operation.</param>
    /// <param name="inExceptionSpecification">A function that returns <see langword="true" /> when the provided Slice
    /// exception conforms to the exception specification; otherwise, <see langword="false" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchOperationAsync<TArgs>(
        this IncomingRequest request,
        Func<IncomingRequest, CancellationToken, ValueTask<TArgs>> decodeArgs,
        Func<TArgs, IFeatureCollection, CancellationToken, ValueTask> method,
        Func<SliceException, bool>? inExceptionSpecification,
        CancellationToken cancellationToken)
    {
        TArgs args = await decodeArgs(request, cancellationToken).ConfigureAwait(false);
        try
        {
            await method(args, request.Features, cancellationToken).ConfigureAwait(false);
            return new OutgoingResponse(request);
        }
        catch (SliceException sliceException) when (inExceptionSpecification?.Invoke(sliceException) ?? false)
        {
            return request.CreateSliceExceptionResponse(sliceException, SliceEncoding.Slice1);
        }
    }

    /// <summary>Dispatches an incoming request to a method that matches the request's operation name. The operation
    /// does not accept any arguments and does not return anything.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="decodeEmptyArgs">A function that decodes the empty arguments from the request payload.</param>
    /// <param name="method">The user-provided implementation of the operation.</param>
    /// <param name="inExceptionSpecification">A function that returns <see langword="true" /> when the provided Slice
    /// exception conforms to the exception specification; otherwise, <see langword="false" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that holds the outgoing response.</returns>
    public static async ValueTask<OutgoingResponse> DispatchOperationAsync(
        this IncomingRequest request,
        Func<IncomingRequest, CancellationToken, ValueTask> decodeEmptyArgs,
        Func<IFeatureCollection, CancellationToken, ValueTask> method,
        Func<SliceException, bool>? inExceptionSpecification,
        CancellationToken cancellationToken)
    {
        await decodeEmptyArgs(request, cancellationToken).ConfigureAwait(false);
        try
        {
            await method(request.Features, cancellationToken).ConfigureAwait(false);
            return new OutgoingResponse(request);
        }
        catch (SliceException sliceException) when (inExceptionSpecification?.Invoke(sliceException) ?? false)
        {
            return request.CreateSliceExceptionResponse(sliceException, SliceEncoding.Slice1);
        }
    }

    // The error message includes the inner exception type and message because we don't transmit this inner exception
    // with the response.
    private static string GetErrorMessage(SliceException exception) =>
        exception.InnerException is Exception innerException ?
            $"{exception.Message} This exception was caused by an exception of type '{innerException.GetType()}' with message: {innerException.Message}" :
            exception.Message;
}
