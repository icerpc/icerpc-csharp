// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace IceRpc.Protobuf.RpcMethods.Internal;

/// <summary>The default <see cref="IAsyncStream{T}" /> implementation. It wraps a <see cref="PipeReader" /> and
/// decodes its bytes into Protobuf messages of type <typeparamref name="T"/>.</summary>
internal sealed class AsyncStream<T> : IAsyncStream<T> where T : class, IMessage<T>
{
    private readonly PipeReader _reader;
    private readonly MessageParser<T> _messageParser;
    private readonly int _maxMessageLength;

    // Canceled by Dispose when iteration has started, to unblock any pending ReadAsync.
    private readonly CancellationTokenSource _disposeCts = new();

    private bool _disposed;

    // Set when GetAsyncEnumerator is called. This enforces the single-enumerator contract even if the created
    // enumerator is never advanced.
    private bool _enumeratorCreated;

    // Set when the iterator body starts executing (first MoveNextAsync/DisposeAsync on the enumerator). This lets
    // Dispose distinguish "enumerator was created but never started" from "iteration actually started".
    private bool _iterationStarted;

    /// <summary>Disposes this stream.</summary>
    /// <remarks>This method may be called concurrently with an in-flight <c>MoveNextAsync</c> on the stream's
    /// enumerator: the in-flight read is unblocked and the consumer's <c>MoveNextAsync</c> throws
    /// <see cref="ObjectDisposedException" />. Calling it a second time is a no-op.</remarks>
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
            // No iteration has started (either no enumerator was created, or one was created but never started),
            // hence no pending read can exist. Safe to complete the reader directly from this thread.
            _reader.Complete();
            _disposeCts.Dispose();
        }
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_enumeratorCreated)
        {
            throw new InvalidOperationException($"An {nameof(IAsyncStream<T>)} can only be enumerated once.");
        }
        _enumeratorCreated = true;
        return EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    internal AsyncStream(PipeReader reader, MessageParser<T> messageParser, int maxMessageLength)
    {
        _reader = reader;
        _messageParser = messageParser;
        _maxMessageLength = maxMessageLength;
    }

    private async IAsyncEnumerable<T> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Because this async method returns an IAsyncEnumerable<T>, it only starts executing when the caller starts
        // iterating (calls MoveNextAsync on the enumerator). It does not execute when EnumerateAsync is called, or
        // even when GetAsyncEnumerator is called on the returned IAsyncEnumerable<T>.
        _iterationStarted = true;

        // Link the caller-provided token with our internal dispose token so that Dispose can unblock ReadAsync.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCts.Token);
        CancellationToken linkedToken = linkedCts.Token;

        try
        {
            while (true)
            {
                T? message;
                try
                {
                    message = await _reader.ReadProtobufMessageAsync(
                        _messageParser,
                        _maxMessageLength,
                        linkedToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
                {
                    // Re-issue the cancellation with the caller's token so the OCE that propagates carries the
                    // token the caller passed in (not our internal linkedToken). When _disposed is the only
                    // source, surface dispose-mid-iteration as ObjectDisposedException.
                    cancellationToken.ThrowIfCancellationRequested();
                    // Safe to read _disposed without a barrier: Dispose writes _disposed before calling
                    // _disposeCts.Cancel(), and observing the cancellation here establishes happens-before
                    // with that write.
                    Debug.Assert(_disposed);
                    throw new ObjectDisposedException(nameof(AsyncStream<>), "The stream was disposed while reading.");
                }

                if (message is null)
                {
                    yield break;
                }
                yield return message;
            }
        }
        finally
        {
            _reader.Complete();
        }
    }
}
