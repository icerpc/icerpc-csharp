// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Slic frame reader class reads Slic frames. The implementation uses a pipe to read the Slic frame
    /// header. The frame data is copied from the pipe until the pipe is empty. When empty, the data is directly read
    /// from the read function (typically from the network connection).</summary>
    internal sealed class SlicFrameReader : ISlicFrameReader, IDisposable
    {
        private readonly Pipe _pipe;
        private readonly Func<Memory<byte>, CancellationToken, ValueTask<int>> _readFunc;

        public void Dispose()
        {
            var exception = new ConnectionLostException();
            _pipe.Writer.Complete(exception);
            _pipe.Reader.Complete(exception);
        }

        public async ValueTask ReadFrameDataAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (buffer.IsEmpty)
            {
                return;
            }

            // If there's still data on the pipe reader. Copy the data from the pipe reader.
            ReadResult result = default;
            while (buffer.Length > 0 && _pipe.Reader.TryRead(out result))
            {
                int length = Math.Min(buffer.Length, (int)result.Buffer.Length);
                result.Buffer.Slice(0, length).CopyTo(buffer.Span);
                _pipe.Reader.AdvanceTo(result.Buffer.GetPosition(length));
                buffer = buffer[length..];
            }

            // No more data from the pipe reader, read the remainder directly from the read function to avoid
            // copies.
            while (buffer.Length > 0)
            {
                int length = await _readFunc(buffer, cancel).ConfigureAwait(false);
                if (length == 0)
                {
                    throw new ConnectionLostException();
                }
                buffer = buffer[length..];
            }
        }

        public async ValueTask<(FrameType FrameType, int FrameSize, long? StreamId)> ReadFrameHeaderAsync(
            CancellationToken cancel)
        {
            while (true)
            {
                // Read data from the pipe.
                if (!_pipe.Reader.TryRead(out ReadResult readResult))
                {
                    // Fill the pipe with data read from _readFunc
                    Memory<byte> buffer = _pipe.Writer.GetMemory();
                    int read = await _readFunc(buffer, cancel).ConfigureAwait(false);
                    _pipe.Writer.Advance(read);
                    await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);

                    // Data should now be available unless the read above didn't return any additional data.
                    if (!_pipe.Reader.TryRead(out readResult))
                    {
                        throw new ConnectionLostException();
                    }
                }

                if (TryDecodeHeader(
                        readResult.Buffer,
                        out (FrameType FrameType, int FrameSize, long? StreamId) header,
                        out int consumed))
                {
                    _pipe.Reader.AdvanceTo(readResult.Buffer.GetPosition(consumed));
                    return header;
                }
                else
                {
                    _pipe.Reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                }
            }

            static bool TryDecodeHeader(
                ReadOnlySequence<byte> buffer,
                out (FrameType FrameType, int FrameSize, long? StreamId) header,
                out int consumed)
            {
                header = default;
                consumed = default;

                var decoder = new SliceDecoder(buffer, Encoding.Slice20);

                // Decode the frame type and frame size.
                if (!decoder.TryDecodeByte(out byte frameType) ||
                    !decoder.TryDecodeSize(out header.FrameSize))
                {
                    return false;
                }
                header.FrameType = (FrameType)frameType;

                // If it's a stream frame, try to decode the stream ID
                if (header.FrameType >= FrameType.Stream)
                {
                    consumed = (int)decoder.Consumed;
                    if (!decoder.TryDecodeVarULong(out ulong streamId))
                    {
                        return false;
                    }
                    header.StreamId = (long)streamId;
                    header.FrameSize -= (int)decoder.Consumed - consumed;
                }

                consumed = (int)decoder.Consumed;
                return true;
            }
        }

        internal SlicFrameReader(
            Func<Memory<byte>, CancellationToken, ValueTask<int>> readFunc,
            MemoryPool<byte> pool,
            int minimumSegmentSize)
        {
            _readFunc = readFunc;
            _pipe = new Pipe(new PipeOptions(
                pool: pool,
                minimumSegmentSize: minimumSegmentSize,
                pauseWriterThreshold: 0,
                writerScheduler: PipeScheduler.Inline));
        }

        private enum State : int
        {
            Reading = 1,
            Disposed = 2,
        }
    }
}
