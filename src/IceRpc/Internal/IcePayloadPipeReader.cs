// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>This pipe reader implementation provides a reader to simplifying the reading of the payload from an
    /// incoming Ice request or response. The payload is buffered into an internal pipe. The size is written first to
    /// the internal pipe and it's followed by the payload data read from the network connection pipe reader.</summary>
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

        internal IcePayloadPipeReader(
            ReadOnlySequence<byte> payload,
            ReplyStatus? replyStatus,
            MemoryPool<byte> pool,
            int minimumSegmentSize)
        {
            _pipe = new Pipe(new PipeOptions(
                pool: pool,
                minimumSegmentSize: minimumSegmentSize,
                pauseWriterThreshold: 0,
                writerScheduler: PipeScheduler.Inline));

            // Encode the segment size and eventually the reply status.
            EncodeSegmentSizeAndReplyStatus((int)payload.Length, replyStatus);

            // Copy the payload data to the internal pipe writer.
            while (payload.Length > 0)
            {
                Span<byte> span = _pipe.Writer.GetSpan();
                int copySize = Math.Min((int)payload.Length, span.Length);
                payload.Slice(0, copySize).CopyTo(span);
                _pipe.Writer.Advance(copySize);
                payload = payload.Slice(copySize);
            }

            // No more data to consume for the payload so we complete the internal pipe writer.
            _pipe.Writer.Complete();

            void EncodeSegmentSizeAndReplyStatus(int payloadSize, ReplyStatus? replyStatus)
            {
                var encoder = new SliceEncoder(_pipe.Writer, Encoding.Slice20);
                encoder.EncodeSize(payloadSize);
                if (replyStatus != null && replyStatus > ReplyStatus.UserException)
                {
                    encoder.EncodeReplyStatus(replyStatus.Value);
                }
            }
        }
    }
}
