// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
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
        private int _state;

        public void Dispose()
        {
            if (_state.TrySetFlag(State.Disposed))
            {
                // Don't complete the pipe if it's being used. Completing the reader or writer while reading is in
                // progress would cause bogus data from the pipe recycled buffers to be returned.
                if (!_state.HasFlag(State.Reading))
                {
                    var exception = new ConnectionLostException();
                    _pipe.Writer.Complete(exception);
                    _pipe.Reader.Complete(exception);
                }
            }
        }

        public async ValueTask ReadFrameDataAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            _state.SetState(State.Reading);
            try
            {
                if (_state.HasFlag(State.Disposed))
                {
                    throw new ConnectionLostException();
                }

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
            finally
            {
                if (_state.HasFlag(State.Disposed))
                {
                    await _pipe.Reader.CompleteAsync().ConfigureAwait(false);
                    await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
                }
                _state.ClearFlag(State.Reading);
            }
        }

        public async ValueTask<(FrameType, int, long?)> ReadFrameHeaderAsync(CancellationToken cancel)
        {
            _state.SetState(State.Reading);
            try
            {
                if (_state.HasFlag(State.Disposed))
                {
                    throw new ConnectionLostException();
                }

                ReadResult readResult;

                // Read the frame type
                readResult = await ReadAtLeastAsync(1).ConfigureAwait(false);
                if (readResult.Buffer.IsEmpty)
                {
                    throw new ConnectionLostException();
                }
                var frameType = (FrameType)readResult.Buffer.FirstSpan[0];
                _pipe.Reader.AdvanceTo(readResult.Buffer.GetPosition(1));

                // Read the frame size
                readResult = await ReadAtLeastAsync(1).ConfigureAwait(false);
                if (readResult.Buffer.IsEmpty)
                {
                    throw new ConnectionLostException();
                }
                int frameSizeLength = Slice20Encoding.DecodeSizeLength(readResult.Buffer.FirstSpan[0]);
                if (frameSizeLength > readResult.Buffer.Length - 1)
                {
                    _pipe.Reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    readResult = await ReadAtLeastAsync(frameSizeLength).ConfigureAwait(false);
                    if (readResult.Buffer.Length < frameSizeLength)
                    {
                        throw new ConnectionLostException();
                    }
                }
                int frameSize = DecodeSizeFromSequence(readResult.Buffer);
                _pipe.Reader.AdvanceTo(readResult.Buffer.GetPosition(frameSizeLength));

                if (frameType < FrameType.Stream)
                {
                    return (frameType, frameSize, null);
                }
                else
                {
                    // Read the stream ID
                    int minSize = Math.Min(8, frameSize);
                    readResult = await ReadAtLeastAsync(minSize).ConfigureAwait(false);
                    if (readResult.Buffer.Length < minSize)
                    {
                        throw new ConnectionLostException();
                    }

                    (long streamId, int streamIdSize) = DecodeStreamIdFromSequence(readResult.Buffer);
                    _pipe.Reader.AdvanceTo(readResult.Buffer.GetPosition(streamIdSize));
                    frameSize -= streamIdSize;
                    return (frameType, frameSize, streamId);
                }

                int DecodeSizeFromSequence(ReadOnlySequence<byte> buffer)
                {
                    var decoder = new SliceDecoder(buffer, Encoding.Slice20);
                    return decoder.DecodeFixedLengthSize();
                }

                (long, int) DecodeStreamIdFromSequence(ReadOnlySequence<byte> buffer)
                {
                    var decoder = new SliceDecoder(buffer, Encoding.Slice20);
                    ulong streamId = decoder.DecodeVarULong();
                    return ((long)streamId, SliceEncoder.GetVarULongEncodedSize(streamId));
                }
            }
            finally
            {
                if (_state.HasFlag(State.Disposed))
                {
                    await _pipe.Reader.CompleteAsync().ConfigureAwait(false);
                    await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
                }
                _state.ClearFlag(State.Reading);
            }

            async ValueTask<ReadResult> ReadAtLeastAsync(int minimumSize)
            {
                // Check first if there's enough data buffered on the pipe.
                if (_pipe.Reader.TryRead(out ReadResult readResult) && readResult.Buffer.Length >= minimumSize)
                {
                    return readResult;
                }
                _pipe.Reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);

                // If there isn't, read enough data from _readFunc.
                int count = 0;
                while (count < minimumSize)
                {
                    Memory<byte> buffer = _pipe.Writer.GetMemory();
                    int read = await _readFunc!(buffer, cancel).ConfigureAwait(false);
                    _pipe.Writer.Advance(read);
                    if (read == 0)
                    {
                        await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
                    }
                    count += read;
                }

                // Push it to the pipe.
                await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);

                // Now, read it from the pipe.
                return await _pipe.Reader.ReadAsync(CancellationToken.None).ConfigureAwait(false);
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
