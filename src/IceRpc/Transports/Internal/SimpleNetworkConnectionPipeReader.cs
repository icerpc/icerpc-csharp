// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    internal class SimpleNetworkConnectionPipeReader : PipeReader
    {
        private readonly ISimpleNetworkConnection _connection;
        private readonly Pipe _pipe;

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed) => _pipe.Reader.AdvanceTo(consumed);

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _pipe.Reader.AdvanceTo(consumed, examined);

        /// <inheritdoc/>
        public override void CancelPendingRead() => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Complete(Exception? exception = null)
        {
            _pipe.Writer.Complete(exception);
            _pipe.Reader.Complete(exception);
        }

        /// <inheritdoc/>
        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancel = default)
        {
            if (!_pipe.Reader.TryRead(out ReadResult readResult))
            {
                // Fill the pipe with data read from _readFunc
                Memory<byte> buffer = _pipe.Writer.GetMemory();
                int read = await _connection.ReadAsync(buffer, cancel).ConfigureAwait(false);
                _pipe.Writer.Advance(read);
                await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);

                // Data should now be available unless the read above didn't return any additional data.
                if (!_pipe.Reader.TryRead(out readResult))
                {
                    throw new ConnectionLostException();
                }
            }

            return readResult;
        }

        /// <summary>Reads data in the given buffer and return once the buffer is full. This method bypass the internal
        /// pipe once no more data is available. The data is directly read from the simple network connection, avoiding
        /// a copy into the internal pipe.</summary>
        internal async ValueTask ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (buffer.IsEmpty)
            {
                return;
            }

            // If there's still data on the pipe reader, copy the data from the pipe reader.
            ReadResult result = default;
            while (buffer.Length > 0 && _pipe.Reader.TryRead(out result))
            {
                int length = Math.Min(buffer.Length, (int)result.Buffer.Length);
                result.Buffer.Slice(0, length).CopyTo(buffer.Span);
                _pipe.Reader.AdvanceTo(result.Buffer.GetPosition(length));
                buffer = buffer[length..];
            }

            // No more data from the pipe reader, read the remainder directly from the read function to avoid copies.
            while (buffer.Length > 0)
            {
                int length = await _connection.ReadAsync(buffer, cancel).ConfigureAwait(false);
                if (length == 0)
                {
                    throw new ConnectionLostException();
                }
                buffer = buffer[length..];
            }
        }

        /// <inheritdoc/>
        public override bool TryRead(out ReadResult result) => _pipe.Reader.TryRead(out result);

        internal SimpleNetworkConnectionPipeReader(
            ISimpleNetworkConnection connection,
            MemoryPool<byte> pool,
            int minimumSegmentSize)
        {
            _connection = connection;
            _pipe = new Pipe(new PipeOptions(
                pool: pool,
                minimumSegmentSize: minimumSegmentSize,
                pauseWriterThreshold: 0,
                writerScheduler: PipeScheduler.Inline));
        }
    }
}
