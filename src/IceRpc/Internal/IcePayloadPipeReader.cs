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
        private readonly PipeReader _networkConnectionReader;
        private int _payloadSizeLeftToRead;
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
        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancel) => ReadPayloadAsync(cancel);

        /// <inheritdoc/>
        public override bool TryRead(out ReadResult result) => _pipe.Reader.TryRead(out result);

        internal IcePayloadPipeReader(
            PipeReader networkConnectionReader,
            int payloadSize,
            MemoryPool<byte> pool,
            int minimumSegmentSize)
        {
            _networkConnectionReader = networkConnectionReader;
            _payloadSizeLeftToRead = payloadSize;
            _pipe = new Pipe(new PipeOptions(
                pool: pool,
                minimumSegmentSize: minimumSegmentSize,
                pauseWriterThreshold: 0,
                writerScheduler: PipeScheduler.Inline));

            // First add the payload size to the internal pipe.
            var encoder = new SliceEncoder(_pipe.Writer, Encoding.Slice20);
            encoder.EncodeSize(payloadSize);
        }

        /// <summary>Fetches and copy the full payload from the network connection pipe reader to the internal
        /// pipe.</summary>
        internal async ValueTask FetchFullPayloadAsync(CancellationToken cancel)
        {
            while (_payloadSizeLeftToRead > 0)
            {
                await ReadPayloadAsync(cancel).ConfigureAwait(false);
            }
        }

        private async ValueTask<ReadResult> ReadPayloadAsync(CancellationToken cancel)
        {
            if (!_pipe.Reader.TryRead(out ReadResult readResult))
            {
                // Read and consume at most _payloadSizeLeftToRead bytes from the connection pipe reader.
                ReadResult result = await _networkConnectionReader.ReadAsync(cancel).ConfigureAwait(false);
                int copied = CopySequenceToWriter(result.Buffer, _pipe.Writer, _payloadSizeLeftToRead);
                _networkConnectionReader.AdvanceTo(result.Buffer.GetPosition(copied));
                _payloadSizeLeftToRead -= copied;

                // Flush the read data to the internal pipe.
                await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);

                if (_payloadSizeLeftToRead == 0)
                {
                    // No more data to consume for the payload so it's time to complete the internal pipe writer.
                    await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
                }

                // Data should now be available unless the read above didn't return any additional data.
                _pipe.Reader.TryRead(out readResult);
            }
            return readResult;

            static int CopySequenceToWriter(ReadOnlySequence<byte> buffer, IBufferWriter<byte> writer, int size)
            {
                int copied = Math.Min((int)buffer.Length, size);
                while (size > 0 && buffer.Length > 0)
                {
                    if (buffer.Length > size)
                    {
                        buffer = buffer.Slice(0, size);
                    }

                    Span<byte> span = writer.GetSpan();
                    int copySize = Math.Min((int)buffer.Length, span.Length);
                    buffer.Slice(0, copySize).CopyTo(span);
                    writer.Advance(copySize);
                    size -= copySize;
                    buffer = buffer.Slice(copySize);
                }
                return copied;
            }
        }
    }
}
