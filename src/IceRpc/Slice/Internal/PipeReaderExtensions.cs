// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods to decode payloads carried by a PipeReader.</summary>
    internal static class PipeReaderExtensions
    {
        /// <summary>Reads/decodes a remote exception from a response payload.</summary>
        /// <param name="payload">The incoming payload.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The remote exception.</returns>
        internal static async ValueTask<RemoteException> ReadRemoteExceptionAsync(
            this PipeReader payload,
            IIceDecoderFactory<IceDecoder> iceDecoderFactory,
            Connection connection,
            IInvoker? invoker,
            CancellationToken cancel)
        {
            int segmentSize = await iceDecoderFactory.Encoding.DecodeSegmentSizeAsync(
                payload,
                cancel).ConfigureAwait(false);

            if (segmentSize == 0)
            {
                throw new InvalidDataException("empty remote exception");
            }

            ReadResult readResult = await payload.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

            if (readResult.IsCanceled)
            {
                throw new OperationCanceledException();
            }

            ReadOnlySequence<byte> segment = readResult.Buffer.Slice(0, segmentSize);

            IceDecoder decoder = iceDecoderFactory.CreateIceDecoder(segment, connection, invoker);
            RemoteException exception = decoder.DecodeException();

            if (exception is not UnknownSlicedRemoteException)
            {
                decoder.CheckEndOfBuffer(skipTaggedParams: false);
                payload.AdvanceTo(segment.End);
            }
            // else, we did not decode the full exception from the buffer

            return exception;
        }

        /// <summary>Reads/decodes a value from a payload.</summary>
        /// <paramtype name="TDecoder">The type of the Ice decoder.</paramtype>
        /// <paramtype name="T">The type of the value.</paramtype>
        /// <param name="payload">The payload.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="decodeFunc">The decode function.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The decoded value.</returns>
        internal static async ValueTask<T> ReadValueAsync<TDecoder, T>(
            this PipeReader payload,
            IIceDecoderFactory<TDecoder> iceDecoderFactory,
            DecodeFunc<TDecoder, T> decodeFunc,
            Connection connection,
            IInvoker? invoker,
            CancellationToken cancel) where TDecoder : IceDecoder
        {
            int segmentSize = await iceDecoderFactory.Encoding.DecodeSegmentSizeAsync(
                payload,
                cancel).ConfigureAwait(false);

            ReadOnlySequence<byte> segment;

            if (segmentSize > 0)
            {
                ReadResult readResult =
                    await payload.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                segment = readResult.Buffer.Slice(0, segmentSize);
            }
            else
            {
                // Typically args with only tagged parameters where the sender does not know any tagged param or all
                // the tagged params are null.
                segment = ReadOnlySequence<byte>.Empty;
            }

            TDecoder decoder = iceDecoderFactory.CreateIceDecoder(segment, connection, invoker);
            T value = decodeFunc(decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: true);

            if (segmentSize > 0)
            {
                payload.AdvanceTo(segment.End);
            }
            return value;
        }

        /// <summary>Reads/decodes empty args or a void return value.</summary>
        /// <param name="payload">The incoming payload.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The remote exception.</returns>
        internal static async ValueTask ReadVoidAsync(
            this PipeReader payload,
            IIceDecoderFactory<IceDecoder> iceDecoderFactory,
            CancellationToken cancel)
        {
            if (await iceDecoderFactory.Encoding.DecodeSegmentSizeAsync(
                payload,
                cancel).ConfigureAwait(false) is int segmentSize && segmentSize > 0)
            {
                ReadResult readResult = await payload.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                ReadOnlySequence<byte> segment = readResult.Buffer.Slice(0, segmentSize);

                IceDecoder decoder = iceDecoderFactory.CreateIceDecoder(segment, invoker: null, connection: null);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
                payload.AdvanceTo(segment.End);
            }
        }
    }
}
