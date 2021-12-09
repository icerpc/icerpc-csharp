// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>A PipeWriter decorator where the decoratee is provided later through SetDecoratee.</summary>
    internal class DelayedPipeWriterDecorator : PipeWriter
    {
        public override bool CanGetUnflushedBytes => Decoratee.CanGetUnflushedBytes;
        public override long UnflushedBytes => Decoratee.UnflushedBytes;

        private PipeWriter Decoratee => _decoratee ?? throw new InvalidOperationException("pipe writer not set yet");
        private PipeWriter? _decoratee;

        public override void Advance(int bytes) => Decoratee.Advance(bytes);

        // TODO: we may want to implement and return a DelayedWriteStreamDecorator here.
        // We can't just forward to Decoratee as it's legitimate to call AsStream before the decoratee is set.
        // public override Stream AsStream(bool leaveOpen = false) => Decoratee.AsStream(leaveOpen);

        public override void CancelPendingFlush() => Decoratee.CancelPendingFlush();
        public override void Complete(Exception? exception) => _decoratee?.Complete(exception);
        public override ValueTask CompleteAsync(Exception? exception = default) =>
            _decoratee?.CompleteAsync(exception) ?? default;
        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) =>
           Decoratee.FlushAsync(cancellationToken);

        public override Memory<byte> GetMemory(int sizeHint) => Decoratee.GetMemory(sizeHint);
        public override Span<byte> GetSpan(int sizeHint) => Decoratee.GetSpan(sizeHint);

        public override ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken) => Decoratee.WriteAsync(source, cancellationToken);

        // Can't decorate CopyFromAsync because it's protected internal, not public.

        internal void SetDecoratee(PipeWriter decoratee)
        {
            // TODO: we currently set and reset this decoratee several times when retrying (resending the exact same
            // OutgoingRequest). Is this correct?

            // if (_decoratee != null)
            // {
            //    throw new InvalidOperationException("pipe writer already set");
            // }
            _decoratee = decoratee;
        }
    }
}
