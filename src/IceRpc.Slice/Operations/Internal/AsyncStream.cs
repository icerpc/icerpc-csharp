// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace IceRpc.Slice.Operations.Internal;

/// <summary>The default <see cref="IAsyncStream{T}" /> implementation. It wraps a <see cref="PipeReader" /> and
/// decodes its bytes into elements of type <typeparamref name="T"/> using a read function and a decode function.
/// </summary>
internal sealed class AsyncStream<T> : IAsyncStream<T>
{
    private readonly PipeReader _reader;
    private readonly Func<PipeReader, CancellationToken, ValueTask<ReadResult>> _readFunc;
    private readonly Func<ReadOnlySequence<byte>, IEnumerable<T>> _decodeBufferFunc;

    // Canceled by Dispose when iteration has started, to unblock any pending ReadAsync.
    private readonly CancellationTokenSource _disposeCts = new();

    private bool _disposed;

    private bool _iterationStarted;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_iterationStarted)
        {
            // An enumerator exists. Cancel the dispose token to unblock any pending ReadAsync; the iterator's
            // finally will complete the reader. We must not dispose _disposeCts here: a linked CTS inside the
            // iterator may still hold a registration on _disposeCts.Token.
            _disposeCts.Cancel();
        }
        else
        {
            // No enumerator was ever created, hence no pending read can exist. Safe to complete the reader
            // directly from this thread.
            _reader.Complete();
            _disposeCts.Dispose();
        }
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_iterationStarted)
        {
            throw new InvalidOperationException($"An {nameof(IAsyncStream<T>)} can only be enumerated once.");
        }
        _iterationStarted = true;
        return EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    internal AsyncStream(
        PipeReader reader,
        Func<PipeReader, CancellationToken, ValueTask<ReadResult>> readFunc,
        Func<ReadOnlySequence<byte>, IEnumerable<T>> decodeBufferFunc)
    {
        _reader = reader;
        _readFunc = readFunc;
        _decodeBufferFunc = decodeBufferFunc;
    }

    private async IAsyncEnumerable<T> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Link the caller-provided token with our internal dispose token so that Dispose can unblock ReadAsync.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCts.Token);
        CancellationToken linkedToken = linkedCts.Token;

        try
        {
            while (true)
            {
                ReadResult readResult;

                try
                {
                    readResult = await _readFunc(_reader, linkedToken).ConfigureAwait(false);

                    if (readResult.IsCanceled)
                    {
                        // We never call CancelPendingRead; an interceptor or middleware can but it's not correct.
                        throw new InvalidOperationException("Unexpected call to CancelPendingRead.");
                    }
                    if (readResult.Buffer.IsEmpty)
                    {
                        Debug.Assert(readResult.IsCompleted);
                        yield break;
                    }
                }
                catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
                {
                    // Canceling the cancellation token (caller token or our dispose token) is a normal way to
                    // complete an iteration.
                    yield break;
                }

                IEnumerable<T> elements = _decodeBufferFunc(readResult.Buffer);
                _reader.AdvanceTo(readResult.Buffer.End);

                foreach (T item in elements)
                {
                    if (linkedToken.IsCancellationRequested)
                    {
                        yield break;
                    }
                    yield return item;
                }

                if (readResult.IsCompleted)
                {
                    yield break;
                }
            }
        }
        finally
        {
            _reader.Complete();
        }
    }
}
