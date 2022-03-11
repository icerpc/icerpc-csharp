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

        internal IcePayloadPipeReader(MemoryPool<byte> pool, int minimumSegmentSize)
        {
            _pipe = new Pipe(new PipeOptions(
                pool: pool,
                minimumSegmentSize: minimumSegmentSize,
                pauseWriterThreshold: 0,
                writerScheduler: PipeScheduler.Inline));
        }

        /// <summary>Fetches and copy the full payload from the network connection pipe reader to the internal pip (as a
        /// segment). If it's a reply payload, the reply status is eventually encoded as well (if the payload is a
        /// system exception)</summary>
        internal async ValueTask ReadPayloadAsync(
            PipeReader networkConnectionReader,
            int payloadSize,
            ReplyStatus? replyStatus,
            CancellationToken cancel)
        {
            // Encode the segment size and eventually the reply status.
            EncodeSegmentSizeAndReplyStatus(payloadSize, replyStatus);

            while (payloadSize > 0)
            {
                // Read data from the network connection pipe reader.
                ReadResult readResult = await networkConnectionReader.ReadAsync(cancel).ConfigureAwait(false);

                // Copy the data to the internal pipe writer.
                int copied = CopySequenceToWriter(readResult.Buffer, payloadSize);
                networkConnectionReader.AdvanceTo(readResult.Buffer.GetPosition(copied));
                payloadSize -= copied;
            }

            // No more data to consume for the payload so it's time to complete the internal pipe writer.
            await _pipe.Writer.CompleteAsync().ConfigureAwait(false);

            void EncodeSegmentSizeAndReplyStatus(int payloadSize, ReplyStatus? replyStatus)
            {
                var encoder = new SliceEncoder(_pipe.Writer, Encoding.Slice20);
                encoder.EncodeSize(payloadSize);
                if (replyStatus != null && replyStatus > ReplyStatus.UserException)
                {
                    encoder.EncodeReplyStatus(replyStatus.Value);
                }
            }

            int CopySequenceToWriter(ReadOnlySequence<byte> buffer, int size)
            {
                int copied = Math.Min((int)buffer.Length, size);
                while (size > 0 && buffer.Length > 0)
                {
                    if (buffer.Length > size)
                    {
                        buffer = buffer.Slice(0, size);
                    }

                    Span<byte> span = _pipe.Writer.GetSpan();
                    int copySize = Math.Min((int)buffer.Length, span.Length);
                    buffer.Slice(0, copySize).CopyTo(span);
                    _pipe.Writer.Advance(copySize);
                    size -= copySize;
                    buffer = buffer.Slice(copySize);
                }
                return copied;
            }
        }
    }
}
