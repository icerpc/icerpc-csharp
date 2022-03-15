// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>This pipe reader implementation provides a reader to simplify the reading of the payload from an
    /// incoming Ice request or response. The payload is buffered into an internal pipe. The size is written first as
    /// a varulong followed by the payload.</summary>
    internal sealed class IcePayloadPipeReader : PipeReader
    {
        private readonly Pipe _pipe;

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed) => _pipe.Reader.AdvanceTo(consumed);

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _pipe.Reader.AdvanceTo(consumed, examined);

        /// <inheritdoc/>
        public override void CancelPendingRead() => _pipe.Reader.CancelPendingRead();

        /// <inheritdoc/>
        public override void Complete(Exception? exception = null)
        {
            _pipe.Writer.Complete(exception);
            _pipe.Reader.Complete(exception);
        }

        /// <inheritdoc/>
        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancel) => _pipe.Reader.ReadAsync(cancel);

        /// <inheritdoc/>
        public override bool TryRead(out ReadResult result) => _pipe.Reader.TryRead(out result);

        internal IcePayloadPipeReader(ReadOnlySequence<byte> payload, MemoryPool<byte> pool, int minimumSegmentSize)
        {
            _pipe = new Pipe(new PipeOptions(
                pool: pool,
                minimumSegmentSize: minimumSegmentSize,
                pauseWriterThreshold: 0,
                writerScheduler: PipeScheduler.Inline));

            var encoder = new SliceEncoder(_pipe.Writer, Encoding.Slice20);
            encoder.EncodeSize(checked((int)payload.Length));

            // Copy the payload data to the internal pipe writer.
            if (payload.Length > 0)
            {
                if (payload.IsSingleSegment)
                {
                    _pipe.Writer.Write(payload.FirstSpan);
                }
                else
                {
                    foreach (ReadOnlyMemory<byte> segment in payload)
                    {
                        _pipe.Writer.Write(segment.Span);
                    }
                }
            }

            // No more data to consume for the payload so we complete the internal pipe writer.
            _pipe.Writer.Complete();
        }
    }
}
