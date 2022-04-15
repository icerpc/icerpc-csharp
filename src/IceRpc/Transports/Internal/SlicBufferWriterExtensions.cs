// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
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
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
            encoder.EncodeUInt8((byte)frameType);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;

            if (streamId != null)
            {
                encoder.EncodeVarUInt62((ulong)streamId);
            }
            encode?.Invoke(ref encoder);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
        }

        internal static void EncodeStreamFrameHeader(
            this IBufferWriter<byte> writer,
            long streamId,
            int length,
            bool endStream)
        {
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
            encoder.EncodeUInt8((byte)(endStream ? FrameType.StreamLast : FrameType.Stream));
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encoder.EncodeVarUInt62((ulong)streamId);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos + length), sizePlaceholder);
        }
    }
}
