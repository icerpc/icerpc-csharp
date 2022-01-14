// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Implements a stateless and therefore shareable PipeReader over an empty sequence.</summary>
    /// <remarks>Because this implementation is stateless, it does not implement CancelPendingRead correctly; in
    /// practice this discrepancy should not be noticeable.</remarks>
    internal sealed class EmptyPipeReader : PipeReader
    {
        /// <summary>The shared instance of the empty pipe reader.</summary>
        internal static PipeReader Instance { get; } = new EmptyPipeReader();

        /// <summary>The ReadResult returned by all read methods.</summary>
        private readonly ReadResult _readResult =
            new(ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true);

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed)
        {
            // no-op
        }

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            // no-op
        }

        /// <inheritdoc/>
        public override void CancelPendingRead()
        {
            // no-op
        }

        /// <inheritdoc/>
        public override void Complete(Exception? exception)
        {
            // no-op
        }

        /// <inheritdoc/>
        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken) => new(_readResult);

        /// <inheritdoc/>
        public override bool TryRead(out ReadResult result)
        {
            result = _readResult;
            return true;
        }

        private EmptyPipeReader()
        {
            // ensures there is only one instance
        }
    }
}
