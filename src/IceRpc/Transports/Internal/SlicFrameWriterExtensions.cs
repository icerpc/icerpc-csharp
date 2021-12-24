// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

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
                (ref IceEncoder encoder) =>
                {
                    encoder.EncodeVarUInt(version);
                    frame.Encode(ref encoder);
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
            SlicMultiplexedStream stream,
            StreamResetBody frame,
            CancellationToken cancel) =>
            WriteFrameAsync(writer, FrameType.StreamReset, stream, frame.Encode, cancel);

        internal static ValueTask WriteStreamConsumedAsync(
            this ISlicFrameWriter writer,
            SlicMultiplexedStream stream,
            StreamConsumedBody frame,
            CancellationToken cancel) =>
            WriteFrameAsync(writer, FrameType.StreamConsumed, stream, frame.Encode, cancel);

        internal static ValueTask WriteStreamStopSendingAsync(
            this ISlicFrameWriter writer,
            SlicMultiplexedStream stream,
            StreamStopSendingBody frame,
            CancellationToken cancel) =>
            WriteFrameAsync(writer, FrameType.StreamStopSending, stream, frame.Encode, cancel);

        internal static ValueTask WriteUnidirectionalStreamReleasedAsync(
            this ISlicFrameWriter writer,
            SlicMultiplexedStream stream,
            CancellationToken cancel) =>
            WriteFrameAsync(writer, FrameType.UnidirectionalStreamReleased, stream, null, cancel);

        private static async ValueTask WriteFrameAsync(
            ISlicFrameWriter writer,
            FrameType type,
            SlicMultiplexedStream? stream,
            EncodeAction? encode,
            CancellationToken cancel)
        {
            // TODO: ISlicFrameWriter needs a better API!

            var pipe = new Pipe();
            Encode(pipe.Writer);
            bool success = pipe.Reader.TryRead(out ReadResult result);
            Debug.Assert(success);
            Debug.Assert(result.IsCompleted);
            byte[] buffer = result.Buffer.ToArray();
            await pipe.Reader.CompleteAsync().ConfigureAwait(false);

            await writer.WriteFrameAsync(
                stream,
                new ReadOnlyMemory<byte>[] { buffer },
                cancel).ConfigureAwait(false);

            void Encode(PipeWriter writer)
            {
                var encoder = new IceEncoder(writer, Encoding.Ice20);
                encoder.EncodeByte((byte)type);
                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(4);
                int startPos = encoder.EncodedByteCount;

                if (stream != null)
                {
                    encoder.EncodeVarULong((ulong)stream.Id);
                }
                if (encode != null)
                {
                    encode?.Invoke(ref encoder);
                }
                IceEncoder.EncodeSize20(encoder.EncodedByteCount - startPos, sizePlaceholder.Span);

                // TODO: all this copying is naturally temporary
                writer.Complete();
            }
        }
    }
}
