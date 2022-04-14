// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Tests
{
    /// <summary>A payload pipe reader decorator to check if the complete method is called.</summary>
    internal sealed class PayloadPipeReaderDecorator : PipeReader
    {
        public Task<bool> CompleteCalled => _completeCalled.Task;

        public Exception? CompleteException { get; private set; }

        private readonly PipeReader _decoratee;
        private readonly TaskCompletionSource<bool> _completeCalled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void AdvanceTo(SequencePosition consumed) => _decoratee.AdvanceTo(consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _decoratee.AdvanceTo(consumed, examined);

        public override void CancelPendingRead() => _decoratee.CancelPendingRead();

        public override void Complete(Exception? exception = null)
        {
            CompleteException = exception;
            _completeCalled.SetResult(true);
            _decoratee.Complete(exception);
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            _decoratee.ReadAsync(cancellationToken);

        public override bool TryRead(out ReadResult result) => _decoratee.TryRead(out result);

        internal PayloadPipeReaderDecorator(PipeReader decoratee) => _decoratee = decoratee;
    }
}
