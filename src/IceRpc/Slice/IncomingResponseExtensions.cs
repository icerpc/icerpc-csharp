// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>Extension methods to decode the payloads of incoming responses when such payloads are encoded with the
    /// Slice encoding.</summary>
    public static class IncomingResponseExtensions
    {
        /// <summary>Decodes a response with a <see cref="ResultType.Failure"/> result type.</summary>
        /// <param name="response">The incoming response.</param>
        /// <param name="request">The outgoing request.</param>
        /// <param name="defaultActivator">The optional default activator.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The decoded failure.</returns>
        public static ValueTask<RemoteException> DecodeFailureAsync(
            this IncomingResponse response,
            OutgoingRequest request,
            IActivator? defaultActivator = null,
            CancellationToken cancel = default) =>
            response.ResultType == ResultType.Failure ?
                response.DecodeRemoteExceptionAsync(
                    request,
                    response.Protocol.SliceEncoding,
                    request.Features.Get<SliceDecodeOptions>() ??
                        response.Connection.Features.Get<SliceDecodeOptions>(),
                    defaultActivator,
                    prxEncodeOptions: null, // we don't expect proxies in Failures, they are usually DispatchException
                    cancel) :
                throw new ArgumentException(
                    $"{nameof(DecodeFailureAsync)} requires a response with a Failure result type",
                    nameof(response));

        /// <summary>Decodes a response payload.</summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="response">The incoming response.</param>
        /// <param name="request">The outgoing request.</param>
        /// <param name="encoding">The encoding of the response payload.</param>
        /// <param name="defaultActivator">The optional default activator.</param>
        /// <param name="encodeOptions">The encode options of the Prx struct that sent the request.</param>
        /// <param name="decodeFunc">The decode function for the return value.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The return value.</returns>
        public static ValueTask<T> DecodeReturnValueAsync<T>(
            this IncomingResponse response,
            OutgoingRequest request,
            SliceEncoding encoding,
            IActivator? defaultActivator,
            SliceEncodeOptions? encodeOptions,
            DecodeFunc<T> decodeFunc,
            CancellationToken cancel = default)
        {
            SliceDecodeOptions? decodeOptions = request.Features.Get<SliceDecodeOptions>() ??
                response.Connection.Features.Get<SliceDecodeOptions>();

            return response.ResultType == ResultType.Success ?
                response.DecodeValueAsync(
                    encoding,
                    decodeOptions,
                    defaultActivator,
                    defaultInvoker: request.Proxy.Invoker,
                    encodeOptions,
                    decodeFunc,
                    cancel) :
                ThrowRemoteExceptionAsync();

            async ValueTask<T> ThrowRemoteExceptionAsync()
            {
                throw await response.DecodeRemoteExceptionAsync(
                    request,
                    encoding,
                    decodeOptions,
                    defaultActivator,
                    encodeOptions,
                    cancel).ConfigureAwait(false);
            }
        }

        /// <summary>Creates an async enumerable over the payload reader of an incoming response to decode fixed size
        /// streamed elements.</summary>
        /// <param name="response">The incoming response.</param>
        /// <param name="request">The outgoing request.</param>
        /// <param name="encoding">The encoding of the response payload.</param>
        /// <param name="defaultActivator">The optional default activator.</param>
        /// <param name="encodeOptions">The encode options of the Prx struct that sent the request.</param>
        /// <param name="decodeFunc">The function used to decode the streamed member.</param>
        /// <param name="elementSize">The size in bytes of the streamed elements.</param>
        /// <returns>The async enumerable to decode and return the streamed members.</returns>
        public static IAsyncEnumerable<T> DecodeStream<T>(
            this IncomingResponse response,
            OutgoingRequest request,
            SliceEncoding encoding,
            IActivator? defaultActivator,
            SliceEncodeOptions? encodeOptions,
            DecodeFunc<T> decodeFunc,
            int elementSize) =>
            response.ToAsyncEnumerable(
                encoding,
                request.Features.Get<SliceDecodeOptions>() ?? response.Connection.Features.Get<SliceDecodeOptions>(),
                defaultActivator,
                defaultInvoker: request.Proxy.Invoker,
                encodeOptions,
                decodeFunc,
                elementSize);

        /// <summary>Creates an async enumerable over the payload reader of an incoming response to decode variable
        /// size streamed elements.</summary>
        /// <param name="response">The incoming response.</param>
        /// <param name="request">The outgoing request.</param>
        /// <param name="encoding">The encoding of the response payload.</param>
        /// <param name="defaultActivator">The optional default activator.</param>
        /// <param name="encodeOptions">The encode options of the Prx struct that sent the request.</param>
        /// <param name="decodeFunc">The function used to decode the streamed member.</param>
        /// <returns>The async enumerable to decode and return the streamed members.</returns>
        public static IAsyncEnumerable<T> DecodeStream<T>(
            this IncomingResponse response,
            OutgoingRequest request,
            SliceEncoding encoding,
            IActivator? defaultActivator,
            SliceEncodeOptions? encodeOptions,
            DecodeFunc<T> decodeFunc) =>
            response.ToAsyncEnumerable(
                encoding,
                request.Features.Get<SliceDecodeOptions>() ?? response.Connection.Features.Get<SliceDecodeOptions>(),
                defaultActivator,
                defaultInvoker: request.Proxy.Invoker,
                encodeOptions,
                decodeFunc);

        /// <summary>Verifies that a response payload carries no return value or only tagged return values.</summary>
        /// <param name="response">The incoming response.</param>
        /// <param name="request">The outgoing request.</param>
        /// <param name="encoding">The encoding of the response payload.</param>
        /// <param name="defaultActivator">The optional default activator.</param>
        /// <param name="cancel">The cancellation token.</param>
        public static ValueTask DecodeVoidReturnValueAsync(
            this IncomingResponse response,
            OutgoingRequest request,
            SliceEncoding encoding,
            IActivator? defaultActivator,
            CancellationToken cancel = default)
        {
            SliceDecodeOptions? decodeOptions = request.Features.Get<SliceDecodeOptions>() ??
                response.Connection.Features.Get<SliceDecodeOptions>();

            return response.ResultType == ResultType.Success ?
                response.DecodeVoidAsync(encoding, decodeOptions, cancel) :
                ThrowRemoteExceptionAsync();

            async ValueTask ThrowRemoteExceptionAsync()
            {
                throw await response.DecodeRemoteExceptionAsync(
                    request,
                    encoding,
                    decodeOptions,
                    defaultActivator,
                    prxEncodeOptions: null,
                    cancel).ConfigureAwait(false);
            }
        }

        private static async ValueTask<RemoteException> DecodeRemoteExceptionAsync(
            this IncomingResponse response,
            OutgoingRequest request,
            SliceEncoding encoding,
            SliceDecodeOptions? decodeOptions,
            IActivator? defaultActivator,
            SliceEncodeOptions? prxEncodeOptions,
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
                    decodeOptions?.MaxSegmentSize ?? SliceDecodeOptions.Default.MaxSegmentSize,
                    cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

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
                decodeOptions ??= SliceDecodeOptions.Default;

                var decoder = new SliceDecoder(
                    buffer,
                    encoding,
                    response.Connection,
                    decodeOptions.ProxyInvoker ?? request.Proxy.Invoker,
                    prxEncodeOptions,
                    activator: decodeOptions.Activator ?? defaultActivator,
                    maxCollectionAllocation: decodeOptions.MaxCollectionAllocation,
                    maxDepth: decodeOptions.MaxDepth);

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
    }
}
