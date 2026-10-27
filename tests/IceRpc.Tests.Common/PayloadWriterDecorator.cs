// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Buffers;

namespace IceRpc.Tests.Common;

/// <summary>A payload writer decorator that reports how it was completed.</summary>
public sealed class PayloadWriterDecorator : IMultiplexedStreamWriter
{
    /// <summary>Gets a task that completes when this writer is completed, by <see cref="CompleteAsync" /> or
    /// <see cref="Abort" />.</summary>
    public Task Completed => _completedTcs.Task;

    /// <summary>Gets a value indicating whether this writer completed successfully: <see cref="CompleteAsync" /> was
    /// called and did not throw.</summary>
    public bool IsCompletedSuccessfully { get; private set; }

    /// <inheritdoc/>
    public long UnflushedBytes => _decoratee.UnflushedBytes;

    private readonly TaskCompletionSource _completedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly IMultiplexedStreamWriter _decoratee;

    /// <inheritdoc/>
    public void Advance(int count) => _decoratee.Advance(count);

    /// <inheritdoc/>
    public void Abort()
    {
        _decoratee.Abort();
        _completedTcs.TrySetResult();
    }

    /// <inheritdoc/>
    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _decoratee.CompleteAsync(cancellationToken);
            IsCompletedSuccessfully = true;
        }
        finally
        {
            _completedTcs.TrySetResult();
        }
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint = 0) => _decoratee.GetMemory(sizeHint);

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0) => _decoratee.GetSpan(sizeHint);

    /// <inheritdoc/>
    public ValueTask<WriteResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken) => _decoratee.WriteAsync(source, endStream, cancellationToken);

    /// <summary>Constructs a payload writer decorator.</summary>
    /// <param name="decoratee">The decoratee.</param>
    public PayloadWriterDecorator(IMultiplexedStreamWriter decoratee) => _decoratee = decoratee;
}
