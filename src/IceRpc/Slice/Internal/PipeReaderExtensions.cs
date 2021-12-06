// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods to decode payloads carried by a PipeReader.</summary>
    internal static class PipeReaderExtensions
    {
        /// <summary>Reads/decodes a remote exception from a response payload represented by a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The remote exception.</returns>
        /// <remarks>The reader is always completed when this method returns.</remarks>
        internal static async ValueTask<RemoteException> ReadRemoteExceptionAsync(
            this PipeReader reader,
            Connection connection,
            IInvoker? invoker,
            IIceDecoderFactory<IceDecoder> iceDecoderFactory,
            CancellationToken cancel)
        {
            RemoteException result;
            try
            {
                ReadResult readResult = await reader.ReadSegmentAsync(
                    iceDecoderFactory.Encoding,
                    cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                if (readResult.Buffer.IsEmpty)
                {
                    throw new InvalidDataException("empty remote exception");
                }

                IceDecoder decoder = iceDecoderFactory.CreateIceDecoder(readResult.Buffer, connection, invoker);
                result = decoder.DecodeException();

                if (result is not UnknownSlicedRemoteException)
                {
                    decoder.CheckEndOfBuffer(skipTaggedParams: false);
                }
                // else, we did not decode the full exception from the buffer

                reader.AdvanceTo(readResult.Buffer.End);
            }
            catch (Exception ex)
            {
                await reader.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }

            // If there are any bytes in the pipe reader after the exception, we ignore them.
            await reader.CompleteAsync().ConfigureAwait(false);
            return result;
        }

        /// <summary>Reads a segment from a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A read result with the segment read from the reader unless IsCanceled is true.</returns>
        /// <exception cref="InvalidDataException">Thrown when the segment size could not be decoded.</exception>
        /// <remarks>The caller must call AdvanceTo when the returned segment length is greater than 0. This method
        /// never marks the reader as completed.</remarks>
        internal static async ValueTask<ReadResult> ReadSegmentAsync(
            this PipeReader reader,
            IceEncoding encoding,
            CancellationToken cancel)
        {
            // First read the segment size.
            ReadResult readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);

            if (readResult.IsCanceled)
            {
                return readResult;
            }

            int segmentSize;

            if (readResult.Buffer.IsEmpty)
            {
                Debug.Assert(readResult.IsCompleted);
                segmentSize = 0;
            }
            else
            {
                int sizeLength = encoding.DecodeSegmentSizeLength(readResult.Buffer.FirstSpan);

                if (sizeLength > readResult.Buffer.Length)
                {
                    reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    readResult = await reader.ReadAtLeastAsync(sizeLength, cancel).ConfigureAwait(false);

                    if (readResult.IsCanceled)
                    {
                        return readResult;
                    }

                    if (readResult.Buffer.Length < sizeLength)
                    {
                        throw new InvalidDataException("too few bytes in segment size");
                    }
                }

                ReadOnlySequence<byte> buffer = readResult.Buffer.Slice(0, sizeLength);
                segmentSize = DecodeSize(buffer, sizeLength);
                reader.AdvanceTo(buffer.End);
            }

            if (segmentSize == 0)
            {
                return new ReadResult(
                    ReadOnlySequence<byte>.Empty,
                    isCanceled: false,
                    isCompleted: readResult.IsCompleted);
            }

            readResult = await reader.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

            if (readResult.IsCanceled)
            {
                return readResult;
            }

            if (readResult.Buffer.Length < segmentSize)
            {
                throw new InvalidDataException("too few bytes in segment");
            }

            return readResult.Buffer.Length == segmentSize ? readResult :
                new ReadResult(readResult.Buffer.Slice(0, segmentSize), isCanceled: false, isCompleted: false);

            int DecodeSize(ReadOnlySequence<byte> buffer, int sizeLength)
            {
                Span<byte> span = stackalloc byte[sizeLength];
                buffer.CopyTo(span);
                return encoding.DecodeSegmentSize(span);
            }
        }

        /// <summary>Reads/decodes a value from a pipe reader.</summary>
        /// <paramtype name="TDecoder">The type of the Ice decoder.</paramtype>
        /// <paramtype name="T">The type of the value.</paramtype>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="decodeFunc">The decode function.</param>
        /// <param name="hasStream">When true, T is or includes a stream parameter or return value.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The decoded value.</returns>
        /// <remarks>This method marks the reader as completed when this method throws an exception or when it succeeds
        /// and hasStream is false. When this methods returns a T with a stream, the returned stream is responsible to
        /// complete the pipe reader.</remarks>
        internal static async ValueTask<T> ReadValueAsync<TDecoder, T>(
            this PipeReader reader,
            Connection connection,
            IInvoker? invoker,
            IIceDecoderFactory<TDecoder> iceDecoderFactory,
            DecodeFunc<TDecoder, T> decodeFunc,
            bool hasStream,
            CancellationToken cancel) where TDecoder : IceDecoder
        {
            T value;

            try
            {
                ReadResult readResult = await reader.ReadSegmentAsync(
                    iceDecoderFactory.Encoding,
                    cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                // The segment can be empty, for example args with only tagged parameters where the sender does not know
                // any tagged param or all the tagged params are null. We still decode such an empty segment to make
                // sure decodeFunc is fine with it.

                TDecoder decoder = iceDecoderFactory.CreateIceDecoder(readResult.Buffer, connection, invoker);
                value = decodeFunc(decoder);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);

                if (!readResult.Buffer.IsEmpty)
                {
                    reader.AdvanceTo(readResult.Buffer.End);
                }
            }
            catch (Exception ex)
            {
                await reader.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }

            if (!hasStream)
            {
                // If there are actually additional bytes on the pipe reader, we ignore them since we're not expecting
                // a stream.
                await reader.CompleteAsync().ConfigureAwait(false);
            }
            return value;
        }

        /// <summary>Reads/decodes empty args or a void return value.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <remarks>The reader is always completed when this method returns.</remarks>
        internal static async ValueTask ReadVoidAsync(
            this PipeReader reader,
            IIceDecoderFactory<IceDecoder> iceDecoderFactory,
            CancellationToken cancel)
        {
            try
            {
                ReadResult readResult = await reader.ReadSegmentAsync(
                    iceDecoderFactory.Encoding,
                    cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                if (!readResult.Buffer.IsEmpty)
                {
                    IceDecoder decoder = iceDecoderFactory.CreateIceDecoder(
                        readResult.Buffer,
                        invoker: null,
                        connection: null);
                    decoder.CheckEndOfBuffer(skipTaggedParams: true);
                    reader.AdvanceTo(readResult.Buffer.End);
                }
            }
            catch (Exception ex)
            {
                await reader.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }
            await reader.CompleteAsync().ConfigureAwait(false);
        }

        /// <summary>Creates an async enumerable over a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="decodeFunc">The function used to decode the streamed param.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <remarks>The implementation currently always uses segments.</remarks>
        internal static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            this PipeReader reader,
            Connection connection,
            IInvoker? invoker,
            IIceDecoderFactory<IceDecoder> iceDecoderFactory,
            Func<IceDecoder, T> decodeFunc,
            [EnumeratorCancellation] CancellationToken cancel = default)
        {
            // when CancelPendingRead is called on _reader, ReadSegmentAsync returns a ReadResult with
            // IsCanceled set to true.
            _ = cancel.Register(() => reader.CancelPendingRead());

            while (true)
            {
                // Each iteration decodes a segment with n values.

                ReadResult readResult;

                try
                {
                    readResult = await reader.ReadSegmentAsync(
                        iceDecoderFactory.Encoding,
                        cancel).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await reader.CompleteAsync(ex).ConfigureAwait(false);
                    yield break; // done
                }

                if (readResult.IsCanceled)
                {
                    await reader.CompleteAsync(new OperationCanceledException()).ConfigureAwait(false);
                    yield break; // done
                }

                if (!readResult.Buffer.IsEmpty)
                {
                    // TODO: it would be nice to reuse the same decoder for all iterations
                    IceDecoder decoder = iceDecoderFactory.CreateIceDecoder(readResult.Buffer, connection, invoker);
                    T value = default!;
                    do
                    {
                        try
                        {
                            value = decodeFunc(decoder);
                        }
                        catch (Exception ex)
                        {
                            await reader.CompleteAsync(ex).ConfigureAwait(false);
                            yield break; // done
                        }

                        yield return value;
                    }
                    while (decoder.Pos < readResult.Buffer.Length);
                }

                if (readResult.IsCompleted)
                {
                    await reader.CompleteAsync().ConfigureAwait(false);
                    yield break; // done
                }
            }
        }
    }
}
