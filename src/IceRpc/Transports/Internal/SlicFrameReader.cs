// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Slic frame reader class reads Slic frames.</summary>
    internal sealed class SlicFrameReader : ISlicFrameReader
    {
        private readonly PipeReader _reader;

        public async ValueTask ReadFrameDataAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (buffer.IsEmpty)
            {
                return;
            }

            ReadResult result = await _reader.ReadAtLeastAsync(buffer.Length, cancel).ConfigureAwait(false);
            result.Buffer.Slice(0, buffer.Length).CopyTo(buffer.Span);
            _reader.AdvanceTo(result.Buffer.GetPosition(buffer.Length));
        }

        public async ValueTask<(FrameType, int, long?)> ReadFrameHeaderAsync(CancellationToken cancel)
        {
            while (true)
            {
                ReadResult readResult = await _reader.ReadAtLeastAsync(2, cancel).ConfigureAwait(false);
                try
                {
                    return DecodeSlicHeader(readResult.Buffer);
                }
                catch (InvalidOperationException) // TODO: make EndOfBuffer public?
                {
                    // Ignore, we need additional data to decode the header.
                    _reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                }
            }

            (FrameType, int, long ?) DecodeSlicHeader(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, Encoding.Slice20);
                var frameType = (FrameType)decoder.DecodeByte();
                int frameSize = decoder.DecodeSize();

                (FrameType frameType, int, long?) result;
                if (frameType >= FrameType.Stream)
                {
                    ulong streamId = decoder.DecodeVarULong();
                    result = (frameType, frameSize - SliceEncoder.GetVarULongEncodedSize(streamId), (long)streamId);
                }
                else
                {
                    result = (frameType, frameSize, null);
                }
                _reader.AdvanceTo(buffer.GetPosition(decoder.Consumed));
                return result;
            }
        }

        internal SlicFrameReader(PipeReader reader) => _reader = reader;
    }
}
