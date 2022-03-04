// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Slic frame writer class writes Slic frames.</summary>
    internal sealed class SlicFrameWriter : ISlicFrameWriter
    {
        private readonly SimpleNetworkConnectionPipeWriter _writer;

        public ValueTask WriteFrameAsync(
            FrameType frameType,
            long? streamId,
            EncodeAction? encode,
            CancellationToken cancel)
        {
            Encode(_writer);
            return _writer.WriteAsync(ReadOnlySequence<byte>.Empty, cancel);

            void Encode(PipeWriter writer)
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
        }

        public ValueTask WriteStreamFrameAsync(
            long streamId,
            ReadOnlySequence<byte> source1,
            ReadOnlySequence<byte> source2,
            bool endStream,
            CancellationToken cancel)
        {
            Encode(_writer);
            return _writer.WriteAsync(source1, source2, cancel);

            void Encode(PipeWriter writer)
            {
                var encoder = new SliceEncoder(writer, Encoding.Slice20);
                encoder.EncodeByte((byte)(endStream ? FrameType.StreamLast : FrameType.Stream));
                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(4);
                int startPos = encoder.EncodedByteCount;
                encoder.EncodeVarULong((ulong)streamId);
                Slice20Encoding.EncodeSize(
                    encoder.EncodedByteCount - startPos + (int)source1.Length + (int)source2.Length,
                    sizePlaceholder.Span);
            }
        }

        internal SlicFrameWriter(SimpleNetworkConnectionPipeWriter writer) => _writer = writer;
    }
}
