// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;

namespace IceRpc.Transports.Internal
{
    internal static class SlicFrameWriterExtensions
    {
        internal static ValueTask WriteInitializeAsync(
            this ISlicFrameWriter writer,
            uint version,
            InitializeBody frame,
            CancellationToken cancel) =>
            WriteFrameAsync(
                writer,
                FrameType.Initialize,
                stream: null,
                encoder =>
                {
                    encoder.EncodeVarUInt(version);
                    frame.Encode(encoder);
                },
                cancel);

        internal static ValueTask WriteInitializeAckAsync(
            this ISlicFrameWriter writer,
            InitializeAckBody frame,
            CancellationToken cancel) => WriteFrameAsync(writer, FrameType.InitializeAck, null, frame.Encode, cancel);

        internal static ValueTask WriteVersionAsync(
            this ISlicFrameWriter writer,
            VersionBody frame,
            CancellationToken cancel) => WriteFrameAsync(writer, FrameType.Version, null, frame.Encode, cancel);

        internal static ValueTask WriteStreamResetAsync(
            this ISlicFrameWriter writer,
            SlicMultiplexedNetworkStream stream,
            StreamResetBody frame,
            CancellationToken cancel) =>
            WriteFrameAsync(writer, FrameType.StreamReset, stream, frame.Encode, cancel);

        internal static ValueTask WriteStreamConsumedAsync(
            this ISlicFrameWriter writer,
            SlicMultiplexedNetworkStream stream,
            StreamConsumedBody frame,
            CancellationToken cancel) =>
            WriteFrameAsync(writer, FrameType.StreamConsumed, stream, frame.Encode, cancel);

        internal static ValueTask WriteStreamStopSendingAsync(
            this ISlicFrameWriter writer,
            SlicMultiplexedNetworkStream stream,
            StreamStopSendingBody frame,
            CancellationToken cancel) =>
            WriteFrameAsync(writer, FrameType.StreamStopSending, stream, frame.Encode, cancel);

        private static ValueTask WriteFrameAsync(
            ISlicFrameWriter writer,
            FrameType type,
            SlicMultiplexedNetworkStream? stream,
            Action<IceEncoder> encode,
            CancellationToken cancel)
        {
            var bufferWriter = new BufferWriter();
            var encoder = new Ice20Encoder(bufferWriter);
            encoder.EncodeByte((byte)type);
            BufferWriter.Position sizePos = encoder.StartFixedLengthSize();
            if (stream != null)
            {
                encoder.EncodeVarULong((ulong)stream.Id);
            }
            encode(encoder);
            encoder.EndFixedLengthSize(sizePos);
            return writer.WriteFrameAsync(stream, bufferWriter.Finish(), cancel);
        }
    }
}
