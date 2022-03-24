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
            CancellationToken _)
        {
            _writer.EncodeFrame(frameType, streamId, encodeAction);

            // The write can't be canceled because it would lead to the writing of an incomplete Slic frame.
            return _writer.WriteAsync(
                ReadOnlySequence<byte>.Empty,
                ReadOnlySequence<byte>.Empty,
                CancellationToken.None);
        }

        public ValueTask WriteStreamFrameAsync(
            long streamId,
            ReadOnlySequence<byte> source1,
            ReadOnlySequence<byte> source2,
            bool endStream,
            CancellationToken cancel)
        {
            _writer.EncodeStreamFrameHeader(streamId, (int)(source1.Length + source2.Length), endStream);

            // The write can't be canceled because it would lead to the writing of an incomplete Slic frame.
            return _writer.WriteAsync(source1, source2, CancellationToken.None);
        }

        internal SlicFrameWriter(SimpleNetworkConnectionWriter writer) => _writer = writer;
    }
}
