// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;

namespace IceRpc.Tests.Common;

/// <summary>A payload pipe reader decorator to check if the complete method is called.</summary>
public sealed class PayloadPipeReaderDecorator : PipeReader
{
    /// <summary>A task that completes when the <see cref="PipeReader.Complete(Exception?)"/> is called.</summary>
    public Task<Exception?> Completed => _completed.Task;

    private readonly TaskCompletionSource<Exception?> _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly PipeReader _decoratee;

    /// <inheritdoc/>
    public override void AdvanceTo(SequencePosition consumed) => _decoratee.AdvanceTo(consumed);

    /// <inheritdoc/>
    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _decoratee.AdvanceTo(consumed, examined);

    /// <inheritdoc/>
    public override void CancelPendingRead() => _decoratee.CancelPendingRead();

    /// <inheritdoc/>
    public override void Complete(Exception? exception = null)
    {
        _completed.TrySetResult(exception);
        _decoratee.Complete(exception);
    }

    /// <inheritdoc/>
    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
        _decoratee.ReadAsync(cancellationToken);

    /// <inheritdoc/>
    public override bool TryRead(out ReadResult result) => _decoratee.TryRead(out result);

    /// <summary>Construct a payload pipe reader decorator.</summary>
    /// <param name="decoratee">The decoratee.</param>
    public PayloadPipeReaderDecorator(PipeReader decoratee) => _decoratee = decoratee;
}
