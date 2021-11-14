// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;

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
            int payloadSize = response.Features.GetPayloadSize();

            if (payloadSize > 0)
            {
                using IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(payloadSize);
                Memory<byte> payload = owner.Memory[0..payloadSize];
                await response.PayloadStream.ReadUntilFullAsync(payload, cancel).ConfigureAwait(false);

                IceDecoder decoder = iceDecoderFactory.CreateIceDecoder(payload, response.Connection, invoker);

                if (response.ResultType == ResultType.Failure)
                {
                    throw response.ToRemoteException(decoder);
                }
                else
                {
                    decoder.CheckEndOfBuffer(skipTaggedParams: true);
                }
            }
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

            int payloadSize = response.Features.GetPayloadSize();

            if (payloadSize == 0)
            {
                throw new InvalidDataException("received response with empty payload for non-void operation");
            }

            using IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(payloadSize);
            Memory<byte> payload = owner.Memory[0..payloadSize];
            await response.PayloadStream.ReadUntilFullAsync(payload, cancel).ConfigureAwait(false);

            TDecoder decoder = iceDecoderFactory.CreateIceDecoder(payload, response.Connection, invoker);

            if (response.ResultType == ResultType.Failure)
            {
                throw response.ToRemoteException(decoder);
            }
            else
            {
                T result = decodeFunc(decoder);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
                return result;
            }
        }

        /// <summary>Decodes a remote exception carried by a response. This decoding is special for 1.1-encoded ice1
        /// responses, because the response can carry an ice1 system exception; for other responses, it simply calls
        /// <see cref="IceDecoder.DecodeException"/>.</summary>
        private static RemoteException ToRemoteException(this IncomingResponse response, IceDecoder decoder)
        {
            RemoteException exception =
                response.Protocol == Protocol.Ice1 &&
                response.PayloadEncoding == Encoding.Ice11 &&
                response.Features.Get<ReplyStatus>() is ReplyStatus replyStatus &&
                replyStatus > ReplyStatus.UserException ?
                    decoder.DecodeIce1SystemException(replyStatus) : decoder.DecodeException();

            if (exception is not UnknownSlicedRemoteException)
            {
                decoder.CheckEndOfBuffer(skipTaggedParams: false);
            }
            // else, we did not decode the full exception from the buffer

            return exception;
        }
    }
}
