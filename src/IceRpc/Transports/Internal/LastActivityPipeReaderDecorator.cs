// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    internal class LastActivityPipeReaderDecorator : PipeReader
    {
        private readonly PipeReader _decoratee;
        private readonly Action _updater;

        public override void AdvanceTo(SequencePosition consumed) =>
            _decoratee.AdvanceTo(consumed);
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _decoratee.AdvanceTo(consumed, examined);
        public override void CancelPendingRead() =>
            _decoratee.CancelPendingRead();
        public override void Complete(Exception? exception = null) =>
            _decoratee.Complete(exception);

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadResult result = await _decoratee.ReadAsync(cancellationToken).ConfigureAwait(false);
            _updater();
            return result;
        }

        public override bool TryRead(out ReadResult result) =>
            _decoratee.TryRead(out result);

        internal LastActivityPipeReaderDecorator(PipeReader decoratee, Action updater)
        {
            _decoratee = decoratee;
            _updater = updater;
        }
    }
}
