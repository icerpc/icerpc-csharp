// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Slic frame reader class reads Slic frames from a simple network connection pipe reader.</summary>
    internal sealed class SlicFrameReader : ISlicFrameReader
    {
        public SimpleNetworkConnectionReader NetworkConnectionReader { get; }

        public async ValueTask<(FrameType FrameType, int FrameSize, long? StreamId)> ReadFrameHeaderAsync(
            CancellationToken cancel)
        {
            while (true)
            {
                // Read data from the pipe reader.
                if (!NetworkConnectionReader.TryRead(out ReadOnlySequence<byte> buffer))
                {
                    buffer = await NetworkConnectionReader.ReadAsync(cancel).ConfigureAwait(false);
                }

                if (TryDecodeHeader(
                    buffer,
                    out (FrameType FrameType, int FrameSize, long? StreamId) header,
                    out int consumed))
                {
                    NetworkConnectionReader.AdvanceTo(buffer.GetPosition(consumed));
                    return header;
                }
                else
                {
                    NetworkConnectionReader.AdvanceTo(buffer.Start, buffer.End);
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

        internal SlicFrameReader(SimpleNetworkConnectionReader reader) => NetworkConnectionReader = reader;
    }
}
