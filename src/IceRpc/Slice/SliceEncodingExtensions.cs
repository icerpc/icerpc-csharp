// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>Extension methods for class <see cref="SliceEncoding"/>.</summary>
    public static class SliceEncodingExtensions
    {
        private static readonly ReadOnlySequence<byte> _payloadWithZeroSize = new(new byte[] { 0 });

        /// <summary>Creates an empty payload encoded with this encoding.</summary>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="hasStream">When true, the Slice operation includes a stream in addition to the empty parameters
        /// or void return.</param>
        /// <returns>A new empty payload.</returns>
        public static PipeReader CreateEmptyPayload(this SliceEncoding encoding, bool hasStream = false)
        {
            if (hasStream && encoding == Encoding.Slice11)
            {
                throw new ArgumentException(
                    $"{nameof(hasStream)} must be false when encoding is 1.1", nameof(hasStream));
            }

            return hasStream ? PipeReader.Create(_payloadWithZeroSize) : EmptyPipeReader.Instance;
        }

        /// <summary>Creates a payload source stream from an async enumerable.</summary>
        public static PipeReader CreatePayloadSourceStream<T>(
            this SliceEncoding encoding,
            IAsyncEnumerable<T> asyncEnumerable,
            EncodeAction<T> encodeAction)
        {
            if (encoding == IceRpc.Encoding.Slice11)
            {
                throw new NotSupportedException("streaming is not supported with encoding 1.1");
            }

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

                        // TODO: avoid all this duplicated code
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
                    var encoder = new SliceEncoder(writer, encoding);
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
        public static PipeReader CreatePayloadFromRemoteException(this SliceEncoding encoding, RemoteException exception)
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new SliceEncoder(pipe.Writer, encoding);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;

            // Dispatch exceptions are encoded as "system exceptions" with 1.1.
            if (encoding == IceRpc.Encoding.Slice11 && exception is DispatchException dispatchException)
            {
                DispatchErrorCode errorCode = dispatchException.ErrorCode;

                switch (errorCode)
                {
                    case DispatchErrorCode.ServiceNotFound:
                    case DispatchErrorCode.OperationNotFound:
                        encoder.EncodeReplyStatus(errorCode == DispatchErrorCode.ServiceNotFound ?
                            ReplyStatus.ObjectNotExistException : ReplyStatus.OperationNotExistException);

                        // TODO: pass context to dispatch exception Encode
                        var requestFailed = new RequestFailedExceptionData(path: "/", "", "");
                        requestFailed.Encode(ref encoder);
                        break;

                    default:
                        encoder.EncodeReplyStatus(ReplyStatus.UnknownException);
                        // We encode the error code in the message.
                        encoder.EncodeString($"[{((byte)errorCode).ToString(CultureInfo.InvariantCulture)}] {dispatchException.Message}");
                        break;
                }
            }
            else
            {
                exception.EncodeTrait(ref encoder);
            }

            Slice20Encoding.EncodeSize(encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete(); // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Encodes a fixed-length size into a span.</summary>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="size">The size to encode.</param>
        /// <param name="into">The destination span. This method uses all its bytes.</param>
        public static void EncodeFixedLengthSize(this SliceEncoding encoding, int size, Span<byte> into)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "size must be positive");
            }

            if (encoding == Encoding.Slice11)
            {
                SliceEncoder.EncodeInt(size, into);
            }
            else
            {
                SliceEncoder.EncodeVarULong((ulong)size, into);
            }
        }
    }
}
