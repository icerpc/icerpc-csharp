// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;

namespace IceRpc.Tests.Common;

/// <summary>A payload pipe reader decorator to check if the complete method is called.</summary>
public sealed class PayloadPipeReaderDecorator : PipeReader
{
    public Task<Exception?> Completed => _completed.Task;

    private readonly TaskCompletionSource<Exception?> _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly PipeReader _decoratee;

    public override void AdvanceTo(SequencePosition consumed) => _decoratee.AdvanceTo(consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _decoratee.AdvanceTo(consumed, examined);

    public override void CancelPendingRead() => _decoratee.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        _completed.TrySetResult(exception);
        _decoratee.Complete(exception);
    }

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
        _decoratee.ReadAsync(cancellationToken);

    public override bool TryRead(out ReadResult result) => _decoratee.TryRead(out result);

    public PayloadPipeReaderDecorator(PipeReader decoratee) => _decoratee = decoratee;
}
