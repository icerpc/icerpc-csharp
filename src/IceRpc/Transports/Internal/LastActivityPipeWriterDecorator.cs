// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    internal class LastActivityPipeWriterDecorator : PipeWriter
    {
        private readonly PipeWriter _decoratee;
        private readonly Action _updater;

        public override void Advance(int bytes) =>
            _decoratee.Advance(bytes);

        public override void CancelPendingFlush() =>
            _decoratee.CancelPendingFlush();

        public override void Complete(Exception? exception = null) =>
            _decoratee.Complete(exception);

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            FlushResult result = await _decoratee.FlushAsync(cancellationToken).ConfigureAwait(false);
            _updater();
            return result;
        }

        public override Memory<byte> GetMemory(int sizeHint = 0) =>
            _decoratee.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint = 0) =>
            _decoratee.GetSpan(sizeHint);

        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken = default)
        {
            FlushResult result = await _decoratee.WriteAsync(source, cancellationToken).ConfigureAwait(false);
            _updater();
            return result;
        }

        internal LastActivityPipeWriterDecorator(PipeWriter decoratee, Action updater)
        {
            _decoratee = decoratee;
            _updater = updater;
        }
    }

    internal class LastActivityReadOnlySequencePipeWriterDecorator : ReadOnlySequencePipeWriter
    {
        private readonly PipeWriter _decoratee;
        private readonly ReadOnlySequencePipeWriter? _readOnlySequenceDecoratee;

        private readonly Action _updater;

        public override void Advance(int bytes) =>
            _decoratee.Advance(bytes);

        public override void CancelPendingFlush() =>
            _decoratee.CancelPendingFlush();

        public override void Complete(Exception? exception = null) =>
            _decoratee.Complete(exception);

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            FlushResult result = await _decoratee.FlushAsync(cancellationToken).ConfigureAwait(false);
            _updater();
            return result;
        }

        public override Memory<byte> GetMemory(int sizeHint = 0) =>
            _decoratee.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint = 0) =>
            _decoratee.GetSpan(sizeHint);

        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken = default)
        {
            FlushResult result = await _decoratee.WriteAsync(source, cancellationToken).ConfigureAwait(false);
            _updater();
            return result;
        }

        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool completeWhenDone,
            CancellationToken cancel)
        {
            FlushResult result = await _readOnlySequenceDecoratee!.WriteAsync(
                source,
                completeWhenDone,
                cancel).ConfigureAwait(false);
            _updater();
            return result;
        }

        internal LastActivityReadOnlySequencePipeWriterDecorator(ReadOnlySequencePipeWriter decoratee, Action updater)
        {
            _decoratee = decoratee;
            _readOnlySequenceDecoratee = decoratee;
            _updater = updater;
        }
    }
}
