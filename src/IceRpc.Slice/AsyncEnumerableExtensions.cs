// Copyright (c) ZeroC, Inc.

using System.Diagnostics;
using System.IO.Pipelines;
using ZeroC.Slice;

namespace IceRpc.Slice;

/// <summary>Provides an extension method for <see cref="IAsyncEnumerable{T}" /> to encode elements into a
/// <see cref="PipeReader"/>.</summary>
public static class AsyncEnumerableExtensions
{
    /// <summary>Extension methods for <see cref="IAsyncEnumerable{T}" />.</summary>
    /// <param name="asyncEnumerable">The async enumerable to encode into a stream of bytes.</param>
    extension<T>(IAsyncEnumerable<T> asyncEnumerable)
    {
        /// <summary>Encodes an async enumerable into a stream of bytes represented by a
        /// <see cref="PipeReader"/>.</summary>
        /// <param name="encodeAction">The action used to encode one element.</param>
        /// <param name="useSegments"><see langword="true" /> if an element can be encoded on a variable number
        /// of bytes; otherwise, <see langword="false" />.</param>
        /// <param name="encoding">The Slice encoding to use.</param>
        /// <param name="encodeOptions">The Slice encode options.</param>
        /// <returns>A pipe reader that represents the encoded stream of bytes.</returns>
        /// <remarks>This extension method is used to encode streaming parameters and streaming return values with the
        /// Slice2 encoding.</remarks>
        public PipeReader ToPipeReader(
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
    }

    // Overriding ReadAtLeastAsyncCore or CopyToAsync methods for this reader is not critical since this reader is
    // mostly used by the IceRPC core to copy the encoded data for the enumerable to the network stream. This copy
    // doesn't use these methods.
#pragma warning disable CA1001 // Types that own disposable fields should be disposable.
    private class AsyncEnumerablePipeReader<T> : PipeReader
#pragma warning restore CA1001
    {
        // Disposed in Complete.
        private readonly IAsyncEnumerator<T> _asyncEnumerator;

        // We don't dispose _cts because it's not necessary
        // (see https://github.com/dotnet/runtime/issues/29970#issuecomment-717840778) and we can't easily dispose it
        // when no one is using it since CancelPendingRead can be called by another thread after Complete is called.
        private readonly CancellationTokenSource _cts = new();
        private readonly EncodeAction<T> _encodeAction;
        private readonly SliceEncoding _encoding;
        private bool _isCompleted;
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
            if (!_isCompleted)
            {
                _isCompleted = true;

                // Cancel MoveNextAsync if it's still running.
                _cts.Cancel();

                _pipe.Reader.Complete();
                _pipe.Writer.Complete();

                _ = DisposeEnumeratorAsync();
            }

            async Task DisposeEnumeratorAsync()
            {
                // Make sure MoveNextAsync is completed before disposing the enumerator. Calling DisposeAsync on the
                // enumerator while MoveNextAsync is still running is disallowed.
                if (_moveNext is not null)
                {
                    try
                    {
                        _ = await _moveNext.ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
                await _asyncEnumerator.DisposeAsync().ConfigureAwait(false);
            }
        }

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (!_pipe.Reader.TryRead(out ReadResult readResult))
            {
                // If no more buffered data to read, fill the pipe with new data.

                // If ReadAsync is canceled, cancel the enumerator iteration to ensure MoveNextAsync below completes.
                using CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(
                    cts => ((CancellationTokenSource)cts!).Cancel(),
                    _cts);

                bool hasNext;
                try
                {
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
                        // Flush does not block because the pipe is configured to not pause flush.
                        ValueTask<FlushResult> valueTask = _pipe.Writer.FlushAsync(CancellationToken.None);
                        Debug.Assert(valueTask.IsCompletedSuccessfully);

                        _moveNext = moveNext;
                        // And the next ReadAsync will await _moveNext.
                    }
                    else
                    {
                        // No need to flush the writer, complete takes care of it.
                        _pipe.Writer.Complete();
                    }

                    // There are bytes in the reader or it's completed since we've just flushed or completed the writer.
                    bool ok = _pipe.Reader.TryRead(out readResult);
                    Debug.Assert(ok);
                }
                catch (OperationCanceledException exception)
                {
                    Debug.Assert(exception.CancellationToken == _cts.Token);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_pipe.Reader.TryRead(out readResult) && readResult.IsCanceled)
                    {
                        // Ok: return canceled readResult once after calling CancelPendingRead.
                        // Note that we can't return a canceled read result with a bogus buffer since the caller must
                        // be able to call reader.AdvanceTo with this buffer.
                    }
                    else
                    {
                        throw new NotSupportedException(
                            "Cannot resume reading an AsyncEnumerablePipeReader after canceling a ReadAsync " +
                            "or calling CancelPendingRead.");
                    }
                }
            }

            return readResult;

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
                throw new NotSupportedException("Streaming is not supported by the Slice1 encoding.");
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
