// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>Extension methods for <see cref="SliceEncoding"/>.</summary>
    public static class SliceEncodingExtensions
    {
        private static readonly ReadOnlySequence<byte> _sizeZeroPayload = new(new byte[] { 0 });

        /// <summary>Creates a non-empty payload with size 0.</summary>
        /// <param name="encoding">The Slice encoding.</param>
        /// <returns>A non-empty payload with size 0.</returns>
        public static PipeReader CreateSizeZeroPayload(this SliceEncoding encoding)
        {
            if (encoding == SliceEncoding.Slice1)
            {
                throw new NotSupportedException(
                    $"{nameof(CreateSizeZeroPayload)} is only available for stream-capable Slice encodings");

            }
            return PipeReader.Create(_sizeZeroPayload);
        }

        /// <summary>Creates a payload stream from an async enumerable.</summary>
        public static PipeReader CreatePayloadStream<T>(
            this SliceEncoding encoding,
            IAsyncEnumerable<T> asyncEnumerable,
            EncodeAction<T> encodeAction)
        {
            if (encoding == SliceEncoding.Slice1)
            {
                throw new NotSupportedException("streaming is not supported with Slice1");
            }
            return new PayloadStreamPipeReader<T>(encoding, asyncEnumerable, encodeAction);
        }

        private class PayloadStreamPipeReader<T> : PipeReader
        {
            private readonly IAsyncEnumerator<T> _asyncEnumerator;
            private readonly CancellationTokenSource _cancellationSource = new();
            private readonly EncodeAction<T> _encodeAction;
            private readonly SliceEncoding _encoding;
            private readonly int _segmentSizeFlushThreshold;
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

            public override void Complete(Exception? exception = null)
            {
                _cancellationSource.Dispose();
                _pipe.Reader.Complete(exception);
                _pipe.Writer.Complete(exception);
                _ = _asyncEnumerator.DisposeAsync().AsTask();
            }

            public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancel = default)
            {
                // If no more buffered data to read, fill the pipe with new data.
                if (_pipe.Reader.TryRead(out ReadResult readResult))
                {
                    return readResult;
                }
                else
                {
                    bool hasNext;
                    if (_moveNext == null)
                    {
                        hasNext = await _asyncEnumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        hasNext = await _moveNext.ConfigureAwait(false);
                        _moveNext = null;
                    }

                    if (hasNext)
                    {
                        Memory<byte> sizePlaceholder = _pipe.Writer.GetMemory(4)[0..4];
                        _pipe.Writer.Advance(4);

                        int size = 0;
                        ValueTask<bool> moveNext;
                        while (hasNext)
                        {
                            size += EncodeElement(_asyncEnumerator.Current);

                            // If we reached the segment size threshold, it's time to flush the segment.
                            // TODO: allow to configure the size limit?
                            if (size > _segmentSizeFlushThreshold)
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

                            hasNext = moveNext.Result;
                        }

                        SliceEncoder.EncodeVarUInt62((ulong)size, sizePlaceholder.Span);

                        if (hasNext)
                        {
                            await _pipe.Writer.FlushAsync(cancel).ConfigureAwait(false);
                        }
                        else
                        {
                            await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
                        }
                    }
                    else
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

            internal PayloadStreamPipeReader(
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
                _segmentSizeFlushThreshold = 32 * 1024;
                _encodeAction = encodeAction;
                _encoding = encoding;
                _asyncEnumerator = asyncEnumerable.GetAsyncEnumerator(_cancellationSource.Token);
            }
        }
    }
}
