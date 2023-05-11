// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;

namespace IceRpc.Tests.Common;

/// <summary>A payload pipe writer decorator to check if the complete method is called.</summary>
public sealed class PayloadPipeWriterDecorator : PipeWriter
{
    /// <summary>A task that completes when the <see cref="PipeWriter.Complete(Exception?)"/> is called.</summary>
    public Task<Exception?> Completed => _completed.Task;

    private readonly TaskCompletionSource<Exception?> _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly PipeWriter _decoratee;

    /// <inheritdoc/>
    public override void CancelPendingFlush() => _decoratee.CancelPendingFlush();

    /// <inheritdoc/>
    public override void Complete(Exception? exception = null)
    {
        _completed.TrySetResult(exception);
        _decoratee.Complete(exception);
    }

    /// <inheritdoc/>
    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) =>
        _decoratee.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    public override void Advance(int bytes) =>
        _decoratee.Advance(bytes);

    /// <inheritdoc/>
    public override Memory<byte> GetMemory(int sizeHint = 0) =>
        _decoratee.GetMemory(sizeHint);

    /// <inheritdoc/>
    public override Span<byte> GetSpan(int sizeHint = 0) =>
        _decoratee.GetSpan(sizeHint);

    /// <summary>Construct a payload pipe writer decorator.</summary>
    /// <param name="decoratee">The decoratee.</param>
    public PayloadPipeWriterDecorator(PipeWriter decoratee) => _decoratee = decoratee;
}
