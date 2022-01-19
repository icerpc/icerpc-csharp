// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>A PipeWriter decorator where the decoratee is provided later through SetDecoratee.</summary>
#pragma warning disable CA1001 // _stream's DisposeAsync calls CompleteAsync on this class, not the other way around
    internal class DelayedPipeWriterDecorator : PipeWriter, IMultiplexedStreamPipeWriter
#pragma warning restore CA1001
    {
        public override bool CanGetUnflushedBytes => Decoratee.CanGetUnflushedBytes;
        public override long UnflushedBytes => Decoratee.UnflushedBytes;

        private PipeWriter Decoratee => _decoratee ?? throw new InvalidOperationException("pipe writer not set yet");
        private PipeWriter? _decoratee;

        private Stream? _stream;

        public override void Advance(int bytes) => Decoratee.Advance(bytes);

        /// <inheritdoc/>
        public override Stream AsStream(bool leaveOpen = false)
        {
            // AsStream is usually called before the decoratee is set.

            if (leaveOpen)
            {
                throw new ArgumentException($"{nameof(leaveOpen)} must be false", nameof(leaveOpen));
            }
            return _stream ??= new PipeWriterStream(this);
        }

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

        // We use the default implementation for CopyFromAsync: it's protected as a result we can't forward
        // it to Decoratee.

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

        public ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool completeWhenDone,
            CancellationToken cancel) =>
            Decoratee.WriteAsync(source, completeWhenDone, cancel);
    }
}
