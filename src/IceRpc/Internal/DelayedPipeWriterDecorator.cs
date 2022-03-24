// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>A PipeWriter decorator where the decoratee is provided later through SetDecoratee.</summary>
#pragma warning disable CA1001 // _stream's DisposeAsync calls CompleteAsync on this class, not the other way around
    internal class DelayedPipeWriterDecorator : ReadOnlySequencePipeWriter
#pragma warning restore CA1001
    {
        public override bool CanGetUnflushedBytes => Decoratee.CanGetUnflushedBytes;
        public override long UnflushedBytes => Decoratee.UnflushedBytes;

        private PipeWriter Decoratee => _decoratee ?? throw new InvalidOperationException("pipe writer not set yet");
        private PipeWriter? _decoratee;

        public override void Advance(int bytes) => Decoratee.Advance(bytes);

        /// <inheritdoc/>
        public override Stream AsStream(bool leaveOpen = false) =>
            throw new InvalidOperationException("call ToPayloadSinkStream");

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

        public override ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool endStream,
            CancellationToken cancel) =>
            Decoratee.WriteAsync(source, endStream, cancel);

        // We use the default implementation for CopyFromAsync: it's protected as a result we can't forward
        // it to Decoratee.

        internal void SetDecoratee(PipeWriter decoratee) =>
            // Overriding the previous decoratee can occur if a request is being retried.
            _decoratee = decoratee;
    }
}
