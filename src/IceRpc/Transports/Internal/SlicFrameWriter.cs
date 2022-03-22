// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Slic frame writer class writes Slic frames.</summary>
    internal sealed class SlicFrameWriter : ISlicFrameWriter
    {
        private readonly SimpleNetworkConnectionWriter _writer;

        public ValueTask WriteFrameAsync(
            FrameType frameType,
            long? streamId,
            EncodeAction? encodeAction,
            CancellationToken cancel)
        {
            _writer.EncodeFrame(frameType, streamId, encodeAction);
            return _writer.WriteAsync(ReadOnlySequence<byte>.Empty, ReadOnlySequence<byte>.Empty, cancel);
        }

        public ValueTask WriteStreamFrameAsync(
            long streamId,
            ReadOnlySequence<byte> source1,
            ReadOnlySequence<byte> source2,
            bool endStream,
            CancellationToken cancel)
        {
            _writer.EncodeStreamFrameHeader(streamId, (int)(source1.Length + source2.Length), endStream);
            return _writer.WriteAsync(source1, source2, cancel);
        }

        internal SlicFrameWriter(SimpleNetworkConnectionWriter writer) => _writer = writer;
    }
}
