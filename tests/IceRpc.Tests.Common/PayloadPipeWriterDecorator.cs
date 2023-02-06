// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;

namespace IceRpc.Tests.Common;

/// <summary>A payload pipe writer decorator to check if the complete method is called.</summary>
public sealed class PayloadPipeWriterDecorator : PipeWriter
{
    public Task<Exception?> Completed => _completed.Task;

    private readonly TaskCompletionSource<Exception?> _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly PipeWriter _decoratee;

    public override void CancelPendingFlush() => _decoratee.CancelPendingFlush();

    public override void Complete(Exception? exception = null)
    {
        _completed.SetResult(exception);
        _decoratee.Complete(exception);
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) =>
        _decoratee.FlushAsync(cancellationToken);

    public override void Advance(int bytes) =>
        _decoratee.Advance(bytes);

    public override Memory<byte> GetMemory(int sizeHint = 0) =>
        _decoratee.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint = 0) =>
        _decoratee.GetSpan(sizeHint);

    public PayloadPipeWriterDecorator(PipeWriter decoratee) => _decoratee = decoratee;
}
