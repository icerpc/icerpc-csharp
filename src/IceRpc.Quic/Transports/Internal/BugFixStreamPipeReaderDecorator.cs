// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>A PipeReader decorator to workaround a bug from the System.IO.Pipelines StreamPipeReader implementation,
/// see https://github.com/dotnet/runtime/issues/82594.</summary>
internal sealed class BugFixStreamPipeReaderDecorator : PipeReader
{
    private readonly PipeReader _decoratee;

    public override void AdvanceTo(SequencePosition consumed) => _decoratee.AdvanceTo(consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _decoratee.AdvanceTo(consumed, examined);

    public override void CancelPendingRead() => _decoratee.CancelPendingRead();

    public override Task CopyToAsync(Stream destination, CancellationToken cancellationToken) =>
        _decoratee.CopyToAsync(destination, cancellationToken);

    public override Task CopyToAsync(PipeWriter writer, CancellationToken cancellationToken) =>
        _decoratee.CopyToAsync(writer, cancellationToken);

    public override void Complete(Exception? exception = null) => _decoratee.Complete(exception);

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _decoratee.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken != cancellationToken)
        {
            // Workaround
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    public override bool TryRead(out ReadResult result) => _decoratee.TryRead(out result);

    protected override async ValueTask<ReadResult> ReadAtLeastAsyncCore(
        int minimumSize,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _decoratee.ReadAtLeastAsync(minimumSize, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken != cancellationToken)
        {
            // Workaround
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    internal BugFixStreamPipeReaderDecorator(PipeReader decoratee) => _decoratee = decoratee;
}
