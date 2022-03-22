// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Diagnostics;
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
            if (encoding == Encoding.Slice11)
            {
                throw new NotSupportedException("streaming is not supported with encoding 1.1");
            }
            return new PayloadSourceStreamPipeReader<T>(encoding, asyncEnumerable, encodeAction);
        }

        /// <summary>Creates the payload of a response from a remote exception.</summary>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="exception">The remote exception.</param>
        /// <returns>A new payload.</returns>
        public static PipeReader CreatePayloadFromRemoteException(this SliceEncoding encoding, RemoteException exception)
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new SliceEncoder(pipe.Writer, encoding);
            Span<byte> sizePlaceholder = encoding == Encoding.Slice11 ? default : encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;

            if (encoding == Encoding.Slice11 && exception is DispatchException dispatchException)
            {
                encoder.EncodeDispatchExceptionAsSystemException(dispatchException);
            }
            else
            {
                exception.EncodeTrait(ref encoder);
            }

            if (encoding != Encoding.Slice11)
            {
                Slice20Encoding.EncodeSize(encoder.EncodedByteCount - startPos, sizePlaceholder);
            }

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

#pragma warning disable CA1001 // CompleteAsync disposes the cancellation source token.
        private class PayloadSourceStreamPipeReader<T> : PipeReader
#pragma warning restore CA1001
        {
            private readonly IAsyncEnumerator<T> _asyncEnumerator;
            private readonly CancellationTokenSource _cancellationSource = new();
            private readonly EncodeAction<T> _encodeAction;
            private readonly SliceEncoding _encoding;
            private readonly int _segmentSizeFlushThreeshold;
            private Task<bool>? _moveNext;
            private readonly Pipe _pipe;

            public override void AdvanceTo(SequencePosition consumed) => _pipe.Reader.AdvanceTo(consumed);

            public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
                _pipe.Reader.AdvanceTo(consumed, examined);

            public override void CancelPendingRead()
            {
                _pipe.Reader.CancelPendingRead();
                _cancellationSource.Cancel();
            }

            public override ValueTask CompleteAsync(Exception? exception = null)
            {
                _cancellationSource.Dispose();
                _pipe.Reader.Complete(exception);
                _pipe.Writer.Complete(exception);
                return _asyncEnumerator.DisposeAsync();
            }

            public override void Complete(Exception? exception = null) => throw new NotSupportedException();

            public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancel = default)
            {
                // If no more buffered data to read, fill the pipe with new data.
                if (!_pipe.Reader.TryRead(out ReadResult readResult))
                {
                    bool completed;
                    if (_moveNext == null)
                    {
                        completed = !await _asyncEnumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        completed = !await _moveNext.ConfigureAwait(false);
                        _moveNext = null;
                    }

                    if (!completed)
                    {
                        Memory<byte> sizePlaceholder = _pipe.Writer.GetMemory(4)[0..4];
                        _pipe.Writer.Advance(4);

                        int size = 0;
                        ValueTask<bool> moveNext;
                        while (true)
                        {
                            size += EncodeElement(_asyncEnumerator.Current);

                            // If we reached the segment size threeshold, it's time to flush the segment.
                            // TODO: allow to configure the size limit?
                            if (size > _segmentSizeFlushThreeshold)
                            {
                                break;
                            }

                            moveNext = _asyncEnumerator.MoveNextAsync();

                            // If we can't get the element synchronously we save the move next task for the next
                            // ReadAsync call and end the loop to flush the encoded elements.
                            if (!moveNext.IsCompletedSuccessfully)
                            {
                                _moveNext = moveNext.AsTask();
                                break;
                            }

                            // If no more elements, we're done!
                            if (!moveNext.Result)
                            {
                                completed = true;
                                break;
                            }
                        }

                        _encoding.EncodeFixedLengthSize(size, sizePlaceholder.Span);

                        if (!completed)
                        {
                            await _pipe.Writer.FlushAsync(cancel).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        completed = true;
                    }

                    if (completed)
                    {
                        await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
                    }
                }

                return await _pipe.Reader.ReadAsync(cancel).ConfigureAwait(false);

                int EncodeElement(T element)
                {
                    // TODO: An encoder is very lightweight, however, creating an encoder per element seems extreme for
                    // tiny elements. We could instead add the elements to a List<T> and encode elements in batches.
                    var encoder = new SliceEncoder(_pipe.Writer, _encoding);
                    _encodeAction(ref encoder, element);
                    return encoder.EncodedByteCount;
                }
            }

            public override bool TryRead(out ReadResult result) => _pipe.Reader.TryRead(out result);

            internal PayloadSourceStreamPipeReader(
                SliceEncoding encoding,
                IAsyncEnumerable<T> asyncEnumerable,
                EncodeAction<T> encodeAction)
            {
                // TODO: pipe options, pipe pooling
                _pipe = new Pipe(new PipeOptions(
                    pool: MemoryPool<byte>.Shared,
                    minimumSegmentSize: -1,
                    pauseWriterThreshold: 0,
                    writerScheduler: PipeScheduler.Inline));

                // TODO: configure
                _segmentSizeFlushThreeshold = 32 * 1024;
                _encodeAction = encodeAction;
                _encoding = encoding;
                _cancellationSource = new CancellationTokenSource();
                _asyncEnumerator = asyncEnumerable.GetAsyncEnumerator(_cancellationSource.Token);
            }
        }
    }
}
