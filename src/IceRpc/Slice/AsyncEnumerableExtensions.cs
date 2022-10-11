// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Extension methods for <see cref="IAsyncEnumerable{T}" />.</summary>
public static class AsyncEnumerableExtensions
{
    /// <summary>Encodes an async enumerable into a stream of bytes represented by a <see cref="PipeReader"/>.</summary>
    /// <typeparam name="T">The async enumerable element type.</typeparam>
    /// <param name="asyncEnumerable">The async enumerable to encode into a stream of bytes.</param>
    /// <param name="encodeAction">The action used to encode one element.</param>
    /// <param name="useSegments"><see langword="true" /> if an element can be encoded on a variable number of bytes;
    /// otherwise, <see langword="false" />.</param>
    /// <param name="encoding">The Slice encoding to use.</param>
    /// <param name="encodeOptions">The Slice encode options.</param>
    /// <returns>A pipe reader that represents the encoded stream of bytes.</returns>
    public static PipeReader ToPipeReader<T>(
        this IAsyncEnumerable<T> asyncEnumerable,
        EncodeAction<T> encodeAction,
        bool useSegments,
        SliceEncoding encoding = SliceEncoding.Slice2,
        SliceEncodeOptions? encodeOptions = null) =>
        new AsyncEnumerablePipeReader<T>(
            asyncEnumerable,
            encodeAction,
            useSegments,
            encoding,
            encodeOptions);

#pragma warning disable CA1001 // Type owns disposable field(s) '_cts' but is not disposable
    private class AsyncEnumerablePipeReader<T> : PipeReader
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
            // Also returns true when the writer is completed.
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

                if (hasNext && EncodeElements() is Task<bool> moveNext)
                {
                    _moveNext = moveNext;
                    _ = await _pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                    // And the next ReadAsync will await _moveNext.
                }
                else
                {
                    await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
                }
            }

            return await _pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            Task<bool>? EncodeElements()
            {
                var encoder = new SliceEncoder(_pipe.Writer, _encoding);

                Span<byte> sizePlaceholder = default;
                if (_useSegments)
                {
                    sizePlaceholder = encoder.GetPlaceholderSpan(4);
                }

                Task<bool>? result = null;
                bool keepEncoding;

                do
                {
                    _encodeAction(ref encoder, _asyncEnumerator.Current);
                    ValueTask<bool> moveNext = _asyncEnumerator.MoveNextAsync();

                    if (moveNext.IsCompletedSuccessfully)
                    {
                        bool hasNext = moveNext.Result;

                        // If we reached the stream flush threshold, it's time to flush.
                        if (encoder.EncodedByteCount - sizePlaceholder.Length >= _streamFlushThreshold)
                        {
                            result = hasNext ? moveNext.AsTask() : null;
                            keepEncoding = false;
                        }
                        else
                        {
                            keepEncoding = hasNext;
                        }
                    }
                    else
                    {
                        // If we can't get the next element synchronously, we return the move next task and end the loop
                        // to flush the encoded elements.
                        result = moveNext.AsTask();
                        keepEncoding = false;
                    }
                }
                while (keepEncoding);

                if (_useSegments)
                {
                    SliceEncoder.EncodeVarUInt62(
                        (ulong)(encoder.EncodedByteCount - sizePlaceholder.Length),
                        sizePlaceholder);
                }
                return result;
            }
        }

        public override bool TryRead(out ReadResult result) => _pipe.Reader.TryRead(out result);

        internal AsyncEnumerablePipeReader(
            IAsyncEnumerable<T> asyncEnumerable,
            EncodeAction<T> encodeAction,
            bool useSegments,
            SliceEncoding encoding,
            SliceEncodeOptions? encodeOptions)
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
