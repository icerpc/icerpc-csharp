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
        /// <summary>Verifies that a response payload carries no return value or only tagged return values.</summary>
        /// <param name="response">The incoming response.</param>
        /// <param name="encoding">The encoding of the response payload.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="hasStream"><c>true</c> if this void value is followed by a stream parameter; otherwise,
        /// <c>false</c>.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <remarks>This method marks the response as completed when this method throws an exception or when it
        /// succeeds and hasStream is false. If hasStream is true, the caller is responsible to complete the
        /// response.</remarks>
        public static ValueTask CheckVoidReturnValueAsync(
            this IncomingResponse response,
            SliceEncoding encoding,
            IActivator defaultActivator,
            bool hasStream,
            CancellationToken cancel)
        {
            return response.ResultType == ResultType.Success ?
                response.DecodeVoidAsync(encoding, hasStream, cancel) :
                ThrowRemoteExceptionAsync();

            async ValueTask ThrowRemoteExceptionAsync()
            {
                throw await response.DecodeRemoteExceptionAsync(
                    encoding,
                    response.Request.Features.Get<SliceDecodePayloadOptions>() ?? SliceDecodePayloadOptions.Default,
                    defaultActivator,
                    cancel).ConfigureAwait(false);
            }
        }

        /// <summary>Decodes a response with a <see cref="ResultType.Failure"/> result type.</summary>
        /// <param name="response">The incoming response.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The decoded failure.</returns>
        public static ValueTask<RemoteException> DecodeFailureAsync(
            this IncomingResponse response,
            IActivator defaultActivator,
            CancellationToken cancel) =>
            response.ResultType == ResultType.Failure ?
                response.DecodeRemoteExceptionAsync(
                    response.Protocol.SliceEncoding,
                    response.Request.Features.Get<SliceDecodePayloadOptions>() ?? SliceDecodePayloadOptions.Default,
                    defaultActivator,
                    cancel) :
                throw new ArgumentException(
                    $"{nameof(DecodeFailureAsync)} requires a response with a Failure result type",
                    nameof(response));

        /// <summary>Decodes a response payload.</summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="response">The incoming response.</param>
        /// <param name="encoding">The encoding of the response payload.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="decodeFunc">The decode function for the return value.</param>
        /// <param name="hasStream"><c>true</c> if the value is followed by a stream parameter; otherwise,
        /// <c>false</c>.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The return value.</returns>
        /// <remarks>This method marks the response as completed when this method throws an exception or when it
        /// succeeds and hasStream is false. If hasStream is true, the caller is responsible to complete the
        /// response.</remarks>
        public static ValueTask<T> DecodeReturnValueAsync<T>(
            this IncomingResponse response,
            SliceEncoding encoding,
            IActivator defaultActivator,
            DecodeFunc<T> decodeFunc,
            bool hasStream,
            CancellationToken cancel)
        {
            return response.ResultType == ResultType.Success ?
                response.DecodeValueAsync(
                    encoding,
                    response.Request.Features.Get<SliceDecodePayloadOptions>() ?? SliceDecodePayloadOptions.Default,
                    defaultActivator,
                    defaultInvoker: response.Request.Proxy.Invoker,
                    decodeFunc,
                    hasStream,
                    cancel) :
                ThrowRemoteExceptionAsync();

            async ValueTask<T> ThrowRemoteExceptionAsync()
            {
                throw await response.DecodeRemoteExceptionAsync(
                    encoding,
                    response.Request.Features.Get<SliceDecodePayloadOptions>() ?? SliceDecodePayloadOptions.Default,
                    defaultActivator,
                    cancel).ConfigureAwait(false);
            }
        }

        /// <summary>Creates an async enumerable over the payload reader of an incoming response to decode streamed
        /// members.</summary>
        /// <param name="response">The incoming response.</param>
        /// <param name="encoding">The encoding of the response payload.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="decodeFunc">The function used to decode the streamed member.</param>
        /// <returns>The async enumerable to decode and return the streamed members.</returns>
        public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            this IncomingResponse response,
            SliceEncoding encoding,
            IActivator defaultActivator,
            DecodeFunc<T> decodeFunc) =>
            response.ToAsyncEnumerable(
                encoding,
                response.Request.Features.Get<SliceDecodePayloadOptions>() ?? SliceDecodePayloadOptions.Default,
                defaultActivator,
                defaultInvoker: response.Request.Proxy.Invoker,
                decodeFunc);

        private static async ValueTask<RemoteException> DecodeRemoteExceptionAsync(
            this IncomingResponse response,
            SliceEncoding encoding,
            SliceDecodePayloadOptions decodePayloadOptions,
            IActivator defaultActivator,
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
                result.Origin = response;
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
                    response.Connection,
                    decodePayloadOptions.ProxyInvoker ?? response.Request.Proxy.Invoker,
                    decodePayloadOptions.Activator ?? defaultActivator,
                    decodePayloadOptions.MaxDepth);

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
