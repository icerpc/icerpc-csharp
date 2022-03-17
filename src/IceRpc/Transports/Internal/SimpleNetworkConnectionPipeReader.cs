// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
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
        public override void CancelPendingRead() =>
            // Supporting this method would require to create a linked cancellation token source. Since there's no need
            // for now to support this method, we just throw.
            throw new NotSupportedException();

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
                // Fill the pipe with data read from the connection.
                Memory<byte> buffer = _pipe.Writer.GetMemory();
                int read = await _connection.ReadAsync(buffer, cancel).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new ConnectionLostException();
                }
                _pipe.Writer.Advance(read);
                await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);

                // Data should now be available unless the read above didn't return any additional data.
                _pipe.Reader.TryRead(out readResult);
                Debug.Assert(readResult.Buffer.Length > 0);
            }
            return readResult;
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

        /// <summary>Writes <paramref name="byteCount"/> bytes read from this pipe reader or its underlying connection
        /// into <paramref name="bufferWriter"/>.</summary>
        internal ValueTask FillBufferWriterAsync(
            IBufferWriter<byte> bufferWriter,
            int byteCount,
            CancellationToken cancel)
        {
            if (byteCount == 0)
            {
                return default;
            }

            // If there's still data on the pipe reader, copy the data from the pipe reader synchronously.
            if (_pipe.Reader.TryRead(out ReadResult result))
            {
                ReadOnlySequence<byte> buffer = result.Buffer;

                if (buffer.Length > byteCount)
                {
                    buffer = buffer.Slice(0, byteCount);
                }
                else if (buffer.IsEmpty || (result.IsCompleted && buffer.Length < byteCount))
                {
                    throw new ConnectionLostException();
                }

                bufferWriter.Write(buffer);
                _pipe.Reader.AdvanceTo(buffer.End);

                byteCount -= (int)buffer.Length;

                if (byteCount == 0)
                {
                    return default;
                }
            }

            return ReadFromConnectionAsync(byteCount);

            // Read the remaining bytes directly from the connection into the buffer writer.
            async ValueTask ReadFromConnectionAsync(int byteCount)
            {
                do
                {
                    Memory<byte> buffer = bufferWriter.GetMemory();
                    if (buffer.Length > byteCount)
                    {
                        buffer = buffer[0..byteCount];
                    }

                    int length = await _connection.ReadAsync(buffer, cancel).ConfigureAwait(false);
                    if (length == 0)
                    {
                        throw new ConnectionLostException();
                    }
                    bufferWriter.Advance(length);
                    byteCount -= length;
                } while (byteCount > 0);
            }
        }
    }
}
