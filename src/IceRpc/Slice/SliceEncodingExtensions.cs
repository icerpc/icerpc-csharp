// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>Extension methods for <see cref="SliceEncoding"/>.</summary>
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
            if (hasStream && encoding == SliceEncoding.Slice1)
            {
                throw new ArgumentException(
                    $"{nameof(hasStream)} must be false when encoding is 1.1", nameof(hasStream));
            }

            return hasStream ? PipeReader.Create(_payloadWithZeroSize) : EmptyPipeReader.Instance;
        }

        /// <summary>Creates a payload stream from an async enumerable.</summary>
        public static PipeReader CreatePayloadStream<T>(
            this SliceEncoding encoding,
            IAsyncEnumerable<T> asyncEnumerable,
            EncodeAction<T> encodeAction)
        {
            if (encoding == SliceEncoding.Slice1)
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

                using var cancellationSource = new CancellationTokenSource();
                IAsyncEnumerator<T> asyncEnumerator = asyncEnumerable.GetAsyncEnumerator(cancellationSource.Token);
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
                                cancellationSource.Cancel();
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
                    SliceEncoder.EncodeVarULong((ulong)size, sizePlaceholder.Span);
                    try
                    {
                        return await writer.FlushAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        cancellationSource.Cancel();
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
            Span<byte> sizePlaceholder = encoding == SliceEncoding.Slice1 ? default : encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;

            if (encoding == SliceEncoding.Slice1 && exception is DispatchException dispatchException)
            {
                encoder.EncodeDispatchExceptionAsSystemException(dispatchException);
            }
            else
            {
                exception.EncodeTrait(ref encoder);
            }

            if (encoding != SliceEncoding.Slice1)
            {
                SliceEncoder.EncodeVarULong((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
            }

            pipe.Writer.Complete(); // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }
    }
}
