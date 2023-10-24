// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Protobuf;

/// <summary>Provides an extension method for <see cref="IAsyncEnumerable{T}" /> to encode elements into a
/// <see cref="PipeReader"/>.</summary>
public static class AsyncEnumerableExtensions
{
    /// <summary>Encodes an async enumerable into a stream of bytes represented by a <see cref="PipeReader"/>.</summary>
    /// <typeparam name="T">The async enumerable element type.</typeparam>
    /// <param name="asyncEnumerable">The async enumerable to encode into a stream of bytes.</param>
    /// <param name="encodeOptions">The Slice encode options.</param>
    /// <returns>A pipe reader that represents the encoded stream of bytes.</returns>
    /// <remarks>This extension method is used to encode streaming parameters and streaming return values with the
    /// Slice2 encoding.</remarks>
    public static PipeReader ToPipeReader<T>(
        this IAsyncEnumerable<T> asyncEnumerable,
        ProtobufEncodeOptions? encodeOptions = null) where T : IMessage<T> =>
        new AsyncEnumerablePipeReader<T>(asyncEnumerable, encodeOptions);

    // Overriding ReadAtLeastAsyncCore or CopyToAsync methods for this reader is not critical since this reader is
    // mostly used by the IceRPC core to copy the encoded data for the enumerable to the network stream. This copy
    // doesn't use these methods.
#pragma warning disable CA1001 // Types that own disposable fields should be disposable.
    private class AsyncEnumerablePipeReader<T> : PipeReader where T : IMessage<T>
#pragma warning restore CA1001
    {
        // Disposed in Complete.
        private readonly IAsyncEnumerator<T> _asyncEnumerator;

        // We don't dispose _cts because it's not necessary
        // (see https://github.com/dotnet/runtime/issues/29970#issuecomment-717840778) and we can't easily dispose it
        // when no one is using it since CancelPendingRead can be called by another thread after Complete is called.
        private readonly CancellationTokenSource _cts = new();
        private bool _isCompleted;
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
                            "Cannot resume reading an AsyncEnumerablePipeReader after canceling a ReadAsync or calling CancelPendingRead.");
                    }
                }
            }

            return readResult;

            Task<bool>? EncodeElements()
            {
                Task<bool>? result = null;
                bool keepEncoding;
                int written = 0;
                do
                {
                    _pipe.Writer.Write(new Span<byte>([0])); // Not compressed
                    Span<byte> lengthPlaceholder = _pipe.Writer.GetSpan(4);
                    _pipe.Writer.Advance(4);
                    _asyncEnumerator.Current.WriteTo(_pipe.Writer);
                    int length = checked((int)_pipe.Writer.UnflushedBytes - written);
                    written += length;
                    BinaryPrimitives.WriteInt32BigEndian(lengthPlaceholder, length - 5);
                    ValueTask<bool> moveNext = _asyncEnumerator.MoveNextAsync();

                    if (moveNext.IsCompletedSuccessfully)
                    {
                        bool hasNext = moveNext.Result;

                        // If we reached the stream flush threshold, it's time to flush.
                        if (written >= _streamFlushThreshold)
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
                return result;
            }
        }

        public override bool TryRead(out ReadResult result) => _pipe.Reader.TryRead(out result);

        internal AsyncEnumerablePipeReader(
            IAsyncEnumerable<T> asyncEnumerable,
            ProtobufEncodeOptions? encodeOptions)
        {
            encodeOptions ??= ProtobufEncodeOptions.Default;
            _pipe = new Pipe(encodeOptions.PipeOptions);
            _streamFlushThreshold = encodeOptions.StreamFlushThreshold;
            _asyncEnumerator = asyncEnumerable.GetAsyncEnumerator(_cts.Token);
        }
    }
}
