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
        private readonly SimpleNetworkConnectionPipeReader _reader;

        public void Dispose() => _reader.Complete(new ConnectionLostException());

        public ValueTask ReadFrameDataAsync(Memory<byte> buffer, CancellationToken cancel) =>
            // TODO: should buffer actually be a pipe writer to write into?
            _reader.ReadAsync(buffer, cancel);

        public async ValueTask<(FrameType FrameType, int FrameSize, long? StreamId)> ReadFrameHeaderAsync(
            CancellationToken cancel)
        {
            while (true)
            {
                // Read data from the pipe reader.
                if (!_reader.TryRead(out ReadResult readResult))
                {
                    readResult = await _reader.ReadAsync(cancel).ConfigureAwait(false);
                }

                if (TryDecodeHeader(
                        readResult.Buffer,
                        out (FrameType FrameType, int FrameSize, long? StreamId) header,
                        out int consumed))
                {
                    _reader.AdvanceTo(readResult.Buffer.GetPosition(consumed));
                    return header;
                }
                else
                {
                    _reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
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

        internal SlicFrameReader(SimpleNetworkConnectionPipeReader reader) => _reader = reader;
    }
}
