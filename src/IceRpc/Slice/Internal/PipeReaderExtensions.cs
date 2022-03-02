// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods to decode payloads carried by a PipeReader.</summary>
    internal static class PipeReaderExtensions
    {
        /// <summary>Decodes the size of a segment from a PipeReader.</summary>
        /// <remarks>The caller does not (and cannot) call AdvanceTo after calling this method.</remarks>
        internal static async ValueTask<(int Size, bool IsCanceled, bool IsCompleted)> DecodeSegmentSizeAsync(
            this PipeReader reader,
            CancellationToken cancel)
        {
            ReadResult readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);

            if (readResult.IsCanceled)
            {
                return (-1, true, false);
            }

            if (readResult.Buffer.IsEmpty)
            {
                Debug.Assert(readResult.IsCompleted);
                reader.AdvanceTo(readResult.Buffer.End);
                return (0, false, true);
            }

            int sizeLength = Slice20Encoding.DecodeSizeLength(readResult.Buffer.FirstSpan[0]);
            if (sizeLength > readResult.Buffer.Length)
            {
                reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                readResult = await reader.ReadAtLeastAsync(sizeLength, cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    return (-1, true, false);
                }

                if (readResult.Buffer.Length < sizeLength)
                {
                    throw new InvalidDataException("too few bytes in segment size");
                }
            }

            ReadOnlySequence<byte> buffer = readResult.Buffer.Slice(readResult.Buffer.Start, sizeLength);
            int size = DecodeSizeFromSequence(buffer);
            bool isCompleted = readResult.IsCompleted && readResult.Buffer.Length == sizeLength;
            reader.AdvanceTo(buffer.End);
            return (size, false, isCompleted);

            int DecodeSizeFromSequence(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, Encoding.Slice20);
                return decoder.DecodeSize();
            }
        }

        /// <summary>Reads/decodes a remote exception from a response payload represented by a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="resultType">The result type.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <param name="activator">The Slice activator.</param>
        /// <param name="maxDepth">The maximum depth when decoding a type recursively.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The remote exception.</returns>
        /// <remarks>The reader is always completed when this method returns.</remarks>
        internal static async ValueTask<RemoteException> ReadRemoteExceptionAsync(
            this PipeReader reader,
            SliceEncoding encoding,
            ResultType resultType,
            Connection connection,
            IInvoker invoker,
            IActivator activator,
            int maxDepth,
            CancellationToken cancel)
        {
            RemoteException result;
            try
            {
                ReadResult readResult = await reader.ReadSegmentAsync(cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                if (readResult.Buffer.IsEmpty)
                {
                    throw new InvalidDataException("empty remote exception");
                }

                result = Decode(readResult.Buffer);
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

            RemoteException Decode(ReadOnlySequence<byte> buffer)
            {
                RemoteException remoteException;

                var decoder = new SliceDecoder(
                    buffer,
                    encoding,
                    connection,
                    invoker,
                    activator,
                    maxDepth);
                remoteException = decoder.DecodeException(resultType);

                if (remoteException is not UnknownException)
                {
                    decoder.CheckEndOfBuffer(skipTaggedParams: false);
                }
                // else, we did not decode the full exception from the buffer

                return remoteException;
            }
        }

        /// <summary>Reads a segment from a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A read result with the segment read from the reader unless IsCanceled is true.</returns>
        /// <exception cref="InvalidDataException">Thrown when the segment size could not be decoded.</exception>
        /// <remarks>The caller must call AdvanceTo when the returned segment length is greater than 0. This method
        /// never marks the reader as completed.</remarks>
        internal static async ValueTask<ReadResult> ReadSegmentAsync(this PipeReader reader, CancellationToken cancel)
        {
            (int segmentSize, bool isCanceled, bool isCompleted) =
                await reader.DecodeSegmentSizeAsync(cancel).ConfigureAwait(false);

            if (isCanceled || segmentSize == 0)
            {
                return new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled, isCompleted);
            }

            if (isCompleted)
            {
                throw new InvalidDataException($"no byte in segment with {segmentSize} bytes");
            }

            ReadResult readResult = await reader.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

            if (readResult.IsCanceled)
            {
                return readResult;
            }

            if (readResult.Buffer.Length < segmentSize)
            {
                throw new InvalidDataException($"too few bytes in segment with {segmentSize} bytes");
            }

            return readResult.Buffer.Length == segmentSize ? readResult :
                new ReadResult(readResult.Buffer.Slice(0, segmentSize), isCanceled: false, isCompleted: false);
        }

        /// <summary>Reads/decodes a value from a pipe reader.</summary>
        /// <paramtype name="T">The type of the value.</paramtype>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="activator">The Slice activator.</param>
        /// <param name="maxDepth">The maximum depth when decoding a type recursively.</param>
        /// <param name="decodeFunc">The decode function.</param>
        /// <param name="hasStream"><c>true</c> if the value is followed by a stream parameter;
        /// otherwise, <c>false</c>.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The decoded value.</returns>
        /// <remarks>This method marks the reader as completed when this method throws an exception or when it succeeds
        /// and hasStream is false. When this methods returns a T with a stream, the returned stream is responsible to
        /// complete the pipe reader.</remarks>
        internal static async ValueTask<T> ReadValueAsync<T>(
            this PipeReader reader,
            SliceEncoding encoding,
            Connection connection,
            IInvoker invoker,
            IActivator activator,
            int maxDepth,
            DecodeFunc<T> decodeFunc,
            bool hasStream,
            CancellationToken cancel)
        {
            T value;

            try
            {
                ReadResult readResult = await reader.ReadSegmentAsync(cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                // The segment can be empty, for example args with only tagged parameters where the sender does not know
                // any tagged param or all the tagged params are null. We still decode such an empty segment to make
                // sure decodeFunc is fine with it.
                value = Decode(readResult.Buffer);

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
                // If there are actually additional bytes on the pipe reader, we ignore them. It's possible the sender
                // operation Slice definition specifies a stream parameter that is not specified on the operation local
                // Slice definition.
                await reader.CompleteAsync().ConfigureAwait(false);
            }
            return value;

            T Decode(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(
                    buffer,
                    encoding,
                    connection,
                    invoker,
                    activator,
                    maxDepth);
                T value = decodeFunc(ref decoder);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
                return value;
            }
        }

        /// <summary>Reads/decodes empty args or a void return value.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="hasStream"><c>true</c> if this void value is followed by a stream parameter;
        /// otherwise, <c>false</c>.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <remarks>The reader is always completed when this method returns.</remarks>
        internal static async ValueTask ReadVoidAsync(
            this PipeReader reader,
            SliceEncoding encoding,
            bool hasStream,
            CancellationToken cancel)
        {
            try
            {
                ReadResult readResult = await reader.ReadSegmentAsync(cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                if (!readResult.Buffer.IsEmpty)
                {
                    Decode(readResult.Buffer);
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
                // If there are actually additional bytes on the pipe reader, we ignore them. It's possible the sender
                // operation Slice definition specifies a stream parameter that is not specified on the operation local
                // Slice definition.
                await reader.CompleteAsync().ConfigureAwait(false);
            }

            void Decode(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, encoding);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
            }
        }

        /// <summary>Creates an async enumerable over a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="activator">The Slice activator.</param>
        /// <param name="maxDepth">The maximum depth when decoding a type recursively.</param>
        /// <param name="decodeFunc">The function used to decode the streamed param.</param>
        /// <param name="streamDecoderOptions">The stream decoder options.</param>
        /// <remarks>The implementation currently always uses segments.</remarks>
        internal static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            this PipeReader reader,
            SliceEncoding encoding,
            Connection connection,
            IInvoker invoker,
            IActivator activator,
            int maxDepth,
            DecodeFunc<T> decodeFunc,
            SliceStreamDecoderOptions streamDecoderOptions)
        {
            Func<ReadOnlySequence<byte>, IEnumerable<T>> decodeBufferFunc = buffer =>
            {
                var decoder = new SliceDecoder(
                    buffer,
                    encoding,
                    connection,
                    invoker,
                    activator,
                    maxDepth);

                var items = new List<T>();
                do
                {
                    items.Add(decodeFunc(ref decoder));
                }
                while (decoder.Consumed < buffer.Length);

                return items;
            };

            var streamDecoder = new StreamDecoder<T>(decodeBufferFunc, streamDecoderOptions);

            _ = Task.Run(() => FillWriterAsync(), CancellationToken.None);

            // when CancelPendingRead is called on reader, ReadSegmentAsync returns a ReadResult with IsCanceled
            // set to true.
            return streamDecoder.ReadAsync(() => reader.CancelPendingRead());

            async Task FillWriterAsync()
            {
                while (true)
                {
                    // Each iteration decodes a segment with n values.

                    // If the reader of the async enumerable misbehaves, we can be left "hanging" in a paused
                    // streamDecoder.WriteAsync. The fix is to fix the application code: set the cancellation token
                    // with WithCancellation and cancel when the async enumerable reader is done and the iteration is
                    // not over (= streamDecoder writer is not completed).
                    CancellationToken cancel = CancellationToken.None;

                    ReadResult readResult;

                    try
                    {
                        readResult = await reader.ReadSegmentAsync(cancel).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        streamDecoder.CompleteWriter();
                        await reader.CompleteAsync(ex).ConfigureAwait(false);
                        break; // done
                    }

                    if (readResult.IsCanceled)
                    {
                        streamDecoder.CompleteWriter();

                        var ex = new OperationCanceledException();
                        await reader.CompleteAsync(ex).ConfigureAwait(false);
                        break; // done
                    }

                    bool streamReaderCompleted = false;

                    if (!readResult.Buffer.IsEmpty)
                    {
                        try
                        {
                            streamReaderCompleted = await streamDecoder.WriteAsync(
                                readResult.Buffer,
                                cancel).ConfigureAwait(false);

                            reader.AdvanceTo(readResult.Buffer.End);
                        }
                        catch (Exception ex)
                        {
                            streamDecoder.CompleteWriter();
                            await reader.CompleteAsync(ex).ConfigureAwait(false);
                            break;
                        }
                    }

                    if (streamReaderCompleted || readResult.IsCompleted)
                    {
                        streamDecoder.CompleteWriter();
                        await reader.CompleteAsync().ConfigureAwait(false);
                        break;
                    }
                }
            }
        }
    }
}
