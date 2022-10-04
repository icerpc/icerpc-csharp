// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Extension methods for <see cref="SliceEncoding" />.</summary>
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
    /// <typeparam name="T">The stream element type.</typeparam>
    /// <param name="encoding">The encoding of the payload.</param>
    /// <param name="asyncEnumerable">The async enumerable to encode and stream.</param>
    /// <param name="encodeOptions">The Slice encode options.</param>
    /// <param name="encodeAction">The action used to encode the streamed member.</param>
    /// <param name="useSegments"><see langword="true" /> if we are encoding a stream elements in segments this is the
    /// case when the streamed elements are of variable size; otherwise, <see langword="false" />.</param>
    /// <returns>The pipe reader to read the payload stream for the given async enumerable.</returns>
    public static PipeReader CreatePayloadStream<T>(
        this SliceEncoding encoding,
        IAsyncEnumerable<T> asyncEnumerable,
        SliceEncodeOptions? encodeOptions,
        EncodeAction<T> encodeAction,
        bool useSegments) =>
        new PayloadStreamPipeReader<T>(
            encoding,
            asyncEnumerable,
            encodeOptions,
            encodeAction,
            useSegments);

#pragma warning disable CA1001 // Type owns disposable field(s) '_cts' but is not disposable
    private class PayloadStreamPipeReader<T> : PipeReader
#pragma warning restore CA1001
    {
        private readonly IAsyncEnumerator<T> _asyncEnumerator;
        private readonly CancellationTokenSource _cts = new(); // Disposed by Complete
        private readonly EncodeAction<T> _encodeAction;
        private readonly SliceEncoding _encoding;
        private readonly bool _useSegments;
        private readonly int _streamFlushThreshold;
        private Task<bool>? _moveNext;
        private readonly Pipe _pipe;

        public override void AdvanceTo(SequencePosition consumed) => _pipe.Reader.AdvanceTo(consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _pipe.Reader.AdvanceTo(consumed, examined);

        public override void CancelPendingRead()
        {
            _pipe.Reader.CancelPendingRead();
            _cts.Cancel();
        }

        public override void Complete(Exception? exception = null)
        {
            _cts.Dispose();
            _pipe.Reader.Complete(exception);
            _pipe.Writer.Complete(exception);
            _ = _asyncEnumerator.DisposeAsync().AsTask();
        }

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            // If no more buffered data to read, fill the pipe with new data.
            if (_pipe.Reader.TryRead(out ReadResult readResult))
            {
                return readResult;
            }
            else
            {
                bool hasNext;
                if (_moveNext is null)
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
                    Memory<byte> sizePlaceholder = null;
                    if (_useSegments)
                    {
                        sizePlaceholder = _pipe.Writer.GetMemory(4)[0..4];
                        _pipe.Writer.Advance(4);
                    }

                    int size = 0;
                    ValueTask<bool> moveNext;
                    while (hasNext)
                    {
                        size += EncodeElement(_asyncEnumerator.Current);

                        // If we reached the stream flush threshold, it's time to flush.
                        if (size >= _streamFlushThreshold)
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

                    if (_useSegments)
                    {
                        SliceEncoder.EncodeVarUInt62((ulong)size, sizePlaceholder.Span);
                    }

                    if (hasNext)
                    {
                        await _pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
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

            return await _pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

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
            SliceEncodeOptions? encodeOptions,
            EncodeAction<T> encodeAction,
            bool useSegments)
        {
            if (encoding == SliceEncoding.Slice1)
            {
                throw new NotSupportedException("streaming is not supported with Slice1");
            }

            encodeOptions ??= SliceEncodeOptions.Default;

            _pipe = new Pipe(encodeOptions.PipeOptions);
            _streamFlushThreshold = encodeOptions.StreamFlushThreshold;
            _encodeAction = encodeAction;
            _encoding = encoding;
            _useSegments = useSegments;
            _asyncEnumerator = asyncEnumerable.GetAsyncEnumerator(_cts.Token);
        }
    }
}
