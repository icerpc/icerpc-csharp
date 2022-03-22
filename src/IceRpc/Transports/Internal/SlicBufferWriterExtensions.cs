// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;

namespace IceRpc.Transports.Internal
{
    internal static class SlicBufferWriterExtensions
    {
        internal static void EncodeFrame(
            this IBufferWriter<byte> writer,
            FrameType frameType,
            long? streamId,
            EncodeAction? encode)
        {
            var encoder = new SliceEncoder(writer, Encoding.Slice20);
            encoder.EncodeByte((byte)frameType);
            Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(4);
            int startPos = encoder.EncodedByteCount;

            if (streamId != null)
            {
                encoder.EncodeVarULong((ulong)streamId);
            }
            encode?.Invoke(ref encoder);
            Slice20Encoding.EncodeSize(encoder.EncodedByteCount - startPos, sizePlaceholder.Span);
        }

        internal static void EncodeStreamFrameHeader(
            this IBufferWriter<byte> writer,
            long streamId,
            int length,
            bool endStream)
        {
            var encoder = new SliceEncoder(writer, Encoding.Slice20);
            encoder.EncodeByte((byte)(endStream ? FrameType.StreamLast : FrameType.Stream));
            Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(4);
            int startPos = encoder.EncodedByteCount;
            encoder.EncodeVarULong((ulong)streamId);
            Slice20Encoding.EncodeSize(
                encoder.EncodedByteCount - startPos + length,
                sizePlaceholder.Span);
        }
    }
}
