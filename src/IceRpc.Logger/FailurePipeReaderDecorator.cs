// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Logger;

internal sealed class FailurePipeReaderDecorator : PipeReader
{
    private readonly PipeReader _decoratee;
    private readonly Action<Exception> _failureAction;

    public override void AdvanceTo(SequencePosition consumed) => _decoratee.AdvanceTo(consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _decoratee.AdvanceTo(consumed, examined);

    public override void CancelPendingRead() => _decoratee.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        // TODO: this requires to fix the core to provide the reason of the request payload send failure with
        // Complete(exception) rather than always call Complete() on the payload pipe reader. The failure reported here
        // would either be from the sending or the reading of the payload. We could consider other strategies instead
        // such as instead only report send failures though the PipeWriter.Complete method. Of course, this would
        // require the interceptor to install a pipe writer decorator for this.
        if (exception is not null)
        {
            _failureAction(exception);
        }
        _decoratee.Complete(exception);
    }

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
        _decoratee.ReadAsync(cancellationToken);

    public override bool TryRead(out ReadResult result) => _decoratee.TryRead(out result);

    internal FailurePipeReaderDecorator(PipeReader decoratee, Action<Exception> failureAction)
    {
        _decoratee = decoratee;
        _failureAction = failureAction;
    }
}
