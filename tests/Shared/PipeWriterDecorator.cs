// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Tests
{
    // A decorator that does nothing; derived classes are expected to override some of its methods.
    public class PipeWriterDecorator : PipeWriter
    {
        public override bool CanGetUnflushedBytes => Decoratee.CanGetUnflushedBytes;
        public override long UnflushedBytes => Decoratee.UnflushedBytes;

        protected PipeWriter Decoratee { get; }

        public PipeWriterDecorator(PipeWriter decoratee) => Decoratee = decoratee;

        public override void Advance(int bytes) => Decoratee.Advance(bytes);
        public override Stream AsStream(bool leaveOpen = false) => Decoratee.AsStream(leaveOpen);
        public override void CancelPendingFlush() => Decoratee.CancelPendingFlush();
        public override void Complete(Exception? exception) => Decoratee?.Complete(exception);
        public override ValueTask CompleteAsync(Exception? exception = default) =>
            Decoratee.CompleteAsync(exception);
        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) =>
           Decoratee.FlushAsync(cancellationToken);

        public override Memory<byte> GetMemory(int sizeHint) => Decoratee.GetMemory(sizeHint);
        public override Span<byte> GetSpan(int sizeHint) => Decoratee.GetSpan(sizeHint);

        public override ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken) => Decoratee.WriteAsync(source, cancellationToken);
    }
}
