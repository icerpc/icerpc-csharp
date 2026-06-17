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

    // Set when GetAsyncEnumerator is called. This enforces the single-enumerator contract even if the created
    // enumerator is never advanced.
    private bool _enumeratorCreated;

    // Atomic state used to safely arbitrate ownership of _reader.Complete() between Dispose and the first
    // MoveNextAsync.
    private int _state;

    public void Dispose()
    {
        int original = Interlocked.Exchange(ref _state, (int)State.Disposed);

        switch ((State)original)
        {
            case State.Initial:
                // No iteration could have started (and any future MoveNextAsync will see Disposed and throw).
                // Safe to complete the reader directly from this thread.
                _reader.Complete();
                _disposeCts.Dispose();
                break;

            case State.Iterating:
                // The iterator owns the reader; its finally will complete it. We only signal cancellation here.
                // We must not dispose _disposeCts here: a linked CTS inside the iterator may still hold a
                // registration on _disposeCts.Token.
                _disposeCts.Cancel();
                break;

            case State.Disposed:
                // no-op (Dispose called more than once).
                break;
        }
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken)
    {
        // We don't check for Disposed here: if the stream was disposed, the first MoveNextAsync call on the
        // returned enumerator throws ObjectDisposedException (see EnumerateAsync).
        if (_enumeratorCreated)
        {
            throw new InvalidOperationException($"An {nameof(IAsyncStream<T>)} can only be enumerated once.");
        }
        _enumeratorCreated = true;
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
        // Because this async method returns an IAsyncEnumerable<T>, it only starts executing when the caller starts
        // iterating (calls MoveNextAsync on the enumerator). It does not execute when EnumerateAsync is called, or
        // even when GetAsyncEnumerator is called on the returned IAsyncEnumerable<T>.

        // Atomically claim the reader (Idle -> Iterating). This races with Dispose's atomic transition to Disposed;
        // whichever transition wins from Idle owns _reader.Complete().
        int original = Interlocked.CompareExchange(ref _state, (int)State.Iterating, (int)State.Initial);
        ObjectDisposedException.ThrowIf(original == (int)State.Disposed, this);
        Debug.Assert(original == (int)State.Initial); // _enumeratorCreated forbids a second iteration.

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
                        // An empty buffer means the reader completed with no more bytes, which is how the end of a
                        // stream is signaled. (A zero-size Slice segment is rejected when the segment is read.)
                        Debug.Assert(readResult.IsCompleted);
                        yield break;
                    }
                }
                catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
                {
                    // Re-issue the cancellation with the caller's token so the OCE that propagates carries the
                    // token the caller passed in (not our internal linkedToken). When dispose is the only source,
                    // surface dispose-mid-iteration as ObjectDisposedException.
                    cancellationToken.ThrowIfCancellationRequested();

                    // Safe to read _state without a barrier: Dispose writes State.Disposed before calling
                    // _disposeCts.Cancel(), and observing the cancellation here establishes happens-before
                    // with that write.
                    Debug.Assert(_state == (int)State.Disposed);
                    throw new ObjectDisposedException(nameof(AsyncStream<>), "The stream was disposed while reading.");
                }

                IEnumerable<T> elements = _decodeBufferFunc(readResult.Buffer);
                _reader.AdvanceTo(readResult.Buffer.End);

                foreach (T item in elements)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // No memory barrier needed: this read is just an early-out optimization. If we miss a
                    // concurrent transition to Disposed, the next ReadAsync call observes the cancellation of
                    // _disposeCts (which has its own synchronization) and we surface ObjectDisposedException
                    // from the catch block above.
                    ObjectDisposedException.ThrowIf(_state == (int)State.Disposed, this);
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

    private enum State
    {
        Initial = 0,
        Iterating = 1,
        Disposed = 2
    }
}
