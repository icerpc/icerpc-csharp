// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>Extension methods for class <see cref="IceEncoding"/>.</summary>
    public static class IceEncodingExtensions
    {
        private static readonly ReadOnlySequence<byte> _payloadWithZeroSize = new(new byte[] { 0 });

        /// <summary>Creates an empty payload encoded with this encoding.</summary>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="hasStream">When true, the Slice operation includes a stream in addition to the empty parameters
        /// or void return.</param>
        /// <returns>A new empty payload.</returns>
        public static PipeReader CreateEmptyPayload(this IceEncoding encoding, bool hasStream = false)
        {
            if (hasStream && encoding == Encoding.Ice11)
            {
                throw new ArgumentException(
                    $"{nameof(hasStream)} must be false when encoding is 1.1", nameof(hasStream));
            }

            return hasStream ? PipeReader.Create(_payloadWithZeroSize) : EmptyPipeReader.Instance;
        }

        /// <summary>Creates the payload of a request from the request's arguments. Use this method is for operations
        /// with multiple parameters.</summary>
        /// <typeparam name="T">The type of the operation's parameters.</typeparam>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="args">The arguments to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <param name="classFormat">The class format (1.1 only).</param>
        /// <returns>A new payload.</returns>
        public static PipeReader CreatePayloadFromArgs<T>(
            this IceEncoding encoding,
            in T args,
            TupleEncodeAction<T> encodeAction,
            FormatType classFormat = default) where T : struct
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, encoding, classFormat);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction(ref encoder, in args);
            encoding.EncodeFixedLengthSize(encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates the payload of a request from the request's argument. Use this method when the operation
        /// takes a single parameter.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="arg">The argument to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="EncodeAction{T}"/> that encodes the argument into the
        /// payload.</param>
        /// <param name="classFormat">The class format (1.1 only).</param>
        /// <returns>A new payload.</returns>
        public static PipeReader CreatePayloadFromSingleArg<T>(
            this IceEncoding encoding,
            T arg,
            EncodeAction<T> encodeAction,
            FormatType classFormat = default)
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, encoding, classFormat);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction(ref encoder, arg);
            encoding.EncodeFixedLengthSize(encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates a payload source stream from an async enumerable.</summary>
        public static PipeReader CreatePayloadSourceStream<T>(
            this IceEncoding encoding,
            IAsyncEnumerable<T> asyncEnumerable,
            EncodeAction<T> encodeAction)
        {
            var pipe = new Pipe(); // TODO: pipe options, pipe pooling

            // start writing immediately into background
            Task.Run(() => FillPipeAsync());

            return pipe.Reader;

            async Task FillPipeAsync()
            {
                PipeWriter writer = pipe.Writer;

                using var cancelationSource = new CancellationTokenSource();
                IAsyncEnumerator<T> asyncEnumerator = asyncEnumerable.GetAsyncEnumerator(cancelationSource.Token);
                await using var _ = asyncEnumerator.ConfigureAwait(false);

                Memory<byte> sizePlaceholder = StartSegment();
                int size = 0;

                while (true)
                {
                    ValueTask<bool> moveNext = asyncEnumerator.MoveNextAsync();
                    if (moveNext.IsCompletedSuccessfully)
                    {
                        if (moveNext.Result)
                        {
                            size += EncodeElement(asyncEnumerator.Current);
                        }
                        else
                        {
                            if (size > 0)
                            {
                                await FinishSegmentAsync(size, sizePlaceholder).ConfigureAwait(false);
                            }
                            break; // End iteration
                        }
                    }
                    else
                    {
                        // If we already wrote some elements write the segment now and start a new one.
                        if (size > 0)
                        {
                            FlushResult flushResult = await FinishSegmentAsync(
                                size,
                                sizePlaceholder).ConfigureAwait(false);

                            // nobody can call CancelPendingFlush on this writer
                            Debug.Assert(!flushResult.IsCanceled);

                            if (flushResult.IsCompleted) // reader no longer reading
                            {
                                cancelationSource.Cancel();
                                break; // End iteration
                            }

                            sizePlaceholder = StartSegment();
                            size = 0;
                        }

                        if (await moveNext.ConfigureAwait(false))
                        {
                            size += EncodeElement(asyncEnumerator.Current);
                        }
                        else
                        {
                            break; // End iteration
                        }
                    }

                    // TODO allow to configure the size limit?
                    if (size > 32 * 1024)
                    {
                        FlushResult flushResult = await FinishSegmentAsync(
                                size,
                                sizePlaceholder).ConfigureAwait(false);

                        // nobody can call CancelPendingFlush on this writer
                        Debug.Assert(!flushResult.IsCanceled);

                        if (flushResult.IsCompleted) // reader no longer reading
                        {
                            break; // End iteration
                        }

                        // TODO: why all this duplicated code??
                        sizePlaceholder = StartSegment();
                        size = 0;
                    }
                }

                // Write end of stream
                await writer.CompleteAsync().ConfigureAwait(false);

                int EncodeElement(T element)
                {
                    // TODO: An encoder is very lightweight, however, creating an encoder per element seems extreme
                    // for tiny elements.
                    var encoder = new IceEncoder(writer, encoding);
                    encodeAction(ref encoder, element);
                    return encoder.EncodedByteCount;
                }

                Memory<byte> StartSegment()
                {
                    Memory<byte> sizePlaceholder = writer.GetMemory(4)[0..4];
                    writer.Advance(4);
                    return sizePlaceholder;
                }

                async ValueTask<FlushResult> FinishSegmentAsync(
                    int size,
                    Memory<byte> sizePlaceholder)
                {
                    encoding.EncodeFixedLengthSize(size, sizePlaceholder.Span);
                    try
                    {
                        return await writer.FlushAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        cancelationSource.Cancel();
                        await writer.CompleteAsync(ex).ConfigureAwait(false);
                        throw;
                    }
                }
            }
        }

        /// <summary>Creates the payload of a response from a remote exception.</summary>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="exception">The remote exception.</param>
        /// <returns>A new payload.</returns>
        public static PipeReader CreatePayloadFromRemoteException(this IceEncoding encoding, RemoteException exception)
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, encoding);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encoder.EncodeException(exception);
            encoding.EncodeFixedLengthSize(encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value tuple. Use this
        /// method when the operation returns a tuple.</summary>
        /// <typeparam name="T">The type of the operation's return value tuple.</typeparam>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="returnValueTuple">The return values to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <param name="classFormat">The class format (1.1 only).</param>
        /// <returns>A new payload.</returns>
        public static PipeReader CreatePayloadFromReturnValueTuple<T>(
            this IceEncoding encoding,
            in T returnValueTuple,
            TupleEncodeAction<T> encodeAction,
            FormatType classFormat = default) where T : struct
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, encoding, classFormat);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction(ref encoder, in returnValueTuple);
            encoding.EncodeFixedLengthSize(encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value. Use this method
        /// when the operation returns a single value.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="returnValue">The return value to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="EncodeAction{T}"/> that encodes the argument into the
        /// payload.</param>
        /// <param name="classFormat">The class format (1.1 only).</param>
        /// <returns>A new payload.</returns>
        public static PipeReader CreatePayloadFromSingleReturnValue<T>(
            this IceEncoding encoding,
            T returnValue,
            EncodeAction<T> encodeAction,
            FormatType classFormat = default)
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, encoding, classFormat);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction(ref encoder, returnValue);
            encoding.EncodeFixedLengthSize(encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Encodes a fixed-length size into a span.</summary>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="size">The size to encode.</param>
        /// <param name="into">The destination span. This method uses all its bytes.</param>
        private static void EncodeFixedLengthSize(this IceEncoding encoding, int size, Span<byte> into)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "size must be positive");
            }

            if (encoding == Encoding.Ice11)
            {
                IceEncoder.EncodeInt(size, into);
            }
            else
            {
                IceEncoder.EncodeVarULong((ulong)size, into);
            }
        }
    }
}
