// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Buffers;

namespace IceRpc.Tests.Common;

/// <summary>A payload writer decorator that reports how it was completed.</summary>
public sealed class PayloadWriterDecorator : IMultiplexedStreamWriter
{
    /// <summary>Gets a task that completes when this writer is completed, with <see langword="null" /> when completed
    /// with <see cref="CompleteAsync" /> and the exception otherwise.</summary>
    public Task<Exception?> Completed => _completed.Task;

    /// <inheritdoc/>
    public long UnflushedBytes => _decoratee.UnflushedBytes;

    private readonly TaskCompletionSource<Exception?> _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly IMultiplexedStreamWriter _decoratee;

    /// <inheritdoc/>
    public void Advance(int count) => _decoratee.Advance(count);

    /// <inheritdoc/>
    public void Complete(Exception exception)
    {
        _completed.TrySetResult(exception);
        _decoratee.Complete(exception);
    }

    /// <inheritdoc/>
    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _decoratee.CompleteAsync(cancellationToken);
            _completed.TrySetResult(null);
        }
        catch (Exception exception)
        {
            _completed.TrySetResult(exception);
            throw;
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
