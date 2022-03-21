// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>A helper class to efficiently read a simple network connection. It provides PipeReader-like API but
    /// is not a PipeReader.</summary>
    internal class SimpleNetworkConnectionReader
    {
        private readonly ISimpleNetworkConnection _connection;
        private readonly Pipe _pipe;

        internal SimpleNetworkConnectionReader(
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

        internal void AdvanceTo(SequencePosition consumed) => _pipe.Reader.AdvanceTo(consumed);

        internal void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _pipe.Reader.AdvanceTo(consumed, examined);

        internal void Complete()
        {
            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
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

        internal ValueTask<ReadOnlySequence<byte>> ReadAsync(CancellationToken cancel = default) =>
            ReadAtLeastAsync(minimumSize: 1, cancel);

        /// <summary>Reads and returns bytes from the underlying network connection. The returned buffer has always
        /// at least minimumSize bytes.</summary>
        internal async ValueTask<ReadOnlySequence<byte>> ReadAtLeastAsync(
            int minimumSize,
            CancellationToken cancel = default)
        {
            if (!_pipe.Reader.TryRead(out ReadResult readResult) || readResult.Buffer.Length < minimumSize)
            {
                do
                {
                    // Fill the pipe with data read from the connection.
                    Memory<byte> buffer = _pipe.Writer.GetMemory();
                    int read = await _connection.ReadAsync(buffer, cancel).ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw new ConnectionLostException();
                    }
                    _pipe.Writer.Advance(read);
                    minimumSize -= read;
                }
                while (minimumSize > 0);

                _ = await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);

                _pipe.Reader.TryRead(out readResult);
            }

            return readResult.Buffer;
        }

        internal bool TryRead(out ReadOnlySequence<byte> buffer)
        {
            if (_pipe.Reader.TryRead(out ReadResult readResult))
            {
                buffer = readResult.Buffer;
                return true;
            }
            else
            {
                buffer = default;
                return false;
            }
        }
    }
}
