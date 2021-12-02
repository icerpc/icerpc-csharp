// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>Extension methods to decode the payloads of incoming responses when such payloads are encoded with the
    /// Ice encoding.</summary>
    public static class IncomingResponseExtensions
    {
        /// <summary>Decodes a response when the corresponding operation returns void.</summary>
        /// <param name="response">The incoming response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="cancel">The cancellation token.</param>
        public static async ValueTask CheckVoidReturnValueAsync(
            this IncomingResponse response,
            IInvoker? invoker,
            IIceDecoderFactory<IceDecoder> iceDecoderFactory,
            CancellationToken cancel)
        {
            if (await iceDecoderFactory.Encoding.DecodeSegmentSizeAsync(
                response.Payload,
                cancel).ConfigureAwait(false) is int segmentSize && segmentSize > 0)
            {
                ReadResult readResult =
                    await response.Payload.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                ReadOnlySequence<byte> segment = readResult.Buffer.Slice(0, segmentSize);

                IceDecoder decoder = iceDecoderFactory.CreateIceDecoder(segment, response.Connection, invoker);

                if (response.ResultType == ResultType.Failure)
                {
                    var exception = response.ToRemoteException(decoder);
                    response.Payload.AdvanceTo(segment.End);
                    throw exception;
                }
                else
                {
                    decoder.CheckEndOfBuffer(skipTaggedParams: true);
                    response.Payload.AdvanceTo(segment.End);
                }
            }
            // else successful check
        }

        /// <summary>Decodes a response; only a specific Ice encoding is expected/supported.</summary>
        /// <paramtype name="TDecoder">The type of the Ice decoder.</paramtype>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="response">The incoming response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="decodeFunc">The decode function for the return value.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The return value.</returns>
        public static async ValueTask<T> ToReturnValueAsync<TDecoder, T>(
            this IncomingResponse response,
            IInvoker? invoker,
            IIceDecoderFactory<TDecoder> iceDecoderFactory,
            DecodeFunc<TDecoder, T> decodeFunc,
            CancellationToken cancel) where TDecoder : IceDecoder
        {
            if (response.PayloadEncoding != iceDecoderFactory.Encoding)
            {
                throw new InvalidDataException(@$"cannot decode response payload encoded with {response.PayloadEncoding
                    }; expected a payload encoded with {iceDecoderFactory.Encoding}");
            }

            int segmentSize = await iceDecoderFactory.Encoding.DecodeSegmentSizeAsync(
                response.Payload,
                cancel).ConfigureAwait(false);

            ReadOnlySequence<byte> segment;

            if (segmentSize > 0)
            {
                ReadResult readResult =
                    await response.Payload.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                segment = readResult.Buffer.Slice(0, segmentSize);
            }
            else
            {
                // Typically return with only tagged return values where the sender does not know any tagged return or
                // all the tagged return values are null.
                segment = ReadOnlySequence<byte>.Empty;
            }

            TDecoder decoder = iceDecoderFactory.CreateIceDecoder(segment, response.Connection, invoker);

            if (response.ResultType == ResultType.Failure)
            {
                var exception = response.ToRemoteException(decoder);

                if (segmentSize > 0)
                {
                    response.Payload.AdvanceTo(segment.End);
                }
                throw exception;
            }
            else
            {
                T result = decodeFunc(decoder);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);

                if (segmentSize > 0)
                {
                    response.Payload.AdvanceTo(segment.End);
                }
                return result;
            }
        }

        /// <summary>Decodes a remote exception carried by a response.</summary>
        private static RemoteException ToRemoteException(this IncomingResponse response, IceDecoder decoder)
        {
            // the caller skipped the size

            RemoteException exception = decoder.DecodeException();

            if (exception is not UnknownSlicedRemoteException)
            {
                decoder.CheckEndOfBuffer(skipTaggedParams: false);
            }
            // else, we did not decode the full exception from the buffer

            return exception;
        }
    }
}
