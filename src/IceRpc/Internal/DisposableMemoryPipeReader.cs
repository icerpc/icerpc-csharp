// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Implements a PipeReader over a ReadOnlyMemory{byte} buffer and a disposable object. The disposable
    /// object is disposed as soon as the pipe reader is completed.</summary>
    internal class DisposableMemoryPipeReader : PipeReader
    {
        private readonly SequencePosition _endPosition;
        private readonly IDisposable _disposable;
        private bool _isDisposed;
        private readonly PipeReader _sequencePipeReader;

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            // Recycle as soon as possible:
            if (consumed.Equals(_endPosition))
            {
                Dispose();
            }

            _sequencePipeReader.AdvanceTo(consumed, examined);
        }

        /// <inheritdoc/>
        public override void CancelPendingRead() => _sequencePipeReader.CancelPendingRead();

        /// <inheritdoc/>
        public override void Complete(Exception? exception)
        {
            Dispose();
            _sequencePipeReader.Complete(exception);
        }

        /// <inheritdoc/>
        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken) =>
            _sequencePipeReader.ReadAsync(cancellationToken);

        /// <inheritdoc/>
        public override bool TryRead(out ReadResult result) => _sequencePipeReader.TryRead(out result);

        /// <summary>Constructs a pipe reader over buffer and a disposable object.</summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="disposable">The disposable object.</param>
        internal DisposableMemoryPipeReader(ReadOnlyMemory<byte> buffer, IDisposable disposable)
        {
            _disposable = disposable;
            var sequence = new ReadOnlySequence<byte>(buffer);
            _endPosition = sequence.End;
            _sequencePipeReader = PipeReader.Create(sequence);
        }

        private void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _disposable.Dispose();
            }
        }
    }
}
