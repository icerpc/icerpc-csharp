// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Slic frame reader class reads Slic frames. The implementation uses a pipe to read the Slice frame
    /// header. The frame data is copied from the pipe until the pipe is empty. When empty, the data is directly read
    /// from the read function (typically from the network connection).</summary>
    internal sealed class SlicFrameReader : ISlicFrameReader, IDisposable
    {
        private readonly Pipe _pipe;
        private readonly Func<Memory<byte>, CancellationToken, ValueTask<int>> _readFunc;

        public void Dispose()
        {
            var exception = new ConnectionLostException(new ObjectDisposedException(nameof(SlicFrameReader)));
            _pipe.Reader.Complete(exception);
            _pipe.Writer.Complete(exception);
        }

        public async ValueTask ReadFrameDataAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (buffer.IsEmpty)
            {
                return;
            }

            // If there's still data on the pipe reader. Copy the data from the pipe reader.
            while (buffer.Length > 0 && _pipe.Reader.TryRead(out ReadResult result))
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
                buffer = buffer[length..];
            }
        }

        public async ValueTask<(FrameType, int, long?)> ReadFrameHeaderAsync(CancellationToken cancel)
        {
            while (true)
            {
                // If there's no data available for reading on the pipe reader, we feed the pipe writer with data read
                // from _readFunc.
                if (!_pipe.Reader.TryRead(out ReadResult readResult))
                {
                    // Read data from _readFunc.
                    Memory<byte> buffer = _pipe.Writer.GetMemory();
                    int count = await _readFunc!(buffer, cancel).ConfigureAwait(false);
                    if (count == 0)
                    {
                        throw new ConnectionLostException(new ObjectDisposedException(nameof(SlicFrameReader)));
                    }
                    _pipe.Writer.Advance(count);
                    await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);

                    // Read again, this time data should be available.
                    readResult = await _pipe.Reader.ReadAsync(cancel).ConfigureAwait(false);
                }

                try
                {
                    Debug.Assert(readResult.Buffer.Length > 0);
                    (FrameType type, int dataSize, long? streamId, long consumed) = readResult.Buffer.DecodeHeader();
                    _pipe.Reader.AdvanceTo(readResult.Buffer.GetPosition(consumed));
                    return (type, dataSize, streamId);
                }
                catch (SliceDecoder.EndOfBufferException)
                {
                    // Ignore, we need additional data to decode the header.
                    _pipe.Reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                }
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
    }
}
