// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Tests
{
    /// <summary>A payload pipe writer decorator to check if the complete method is called.</summary>
    internal sealed class PayloadPipeWriterDecorator : PipeWriter
    {
        public Task<bool> CompleteCalled => _completeCalled.Task;
        public Exception? CompleteException { get; private set; }

        private readonly PipeWriter _decoratee;
        private readonly TaskCompletionSource<bool> _completeCalled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void CancelPendingFlush() => _decoratee.CancelPendingFlush();

        public override void Complete(Exception? exception = null)
        {
            CompleteException = exception;
            _completeCalled.SetResult(true);
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

        internal PayloadPipeWriterDecorator(PipeWriter decoratee) => _decoratee = decoratee;
    }
}
