// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Transports.Slic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>The Slic frame writer class writes Slic frames and sends them over the single stream
    /// connection.</summary>
    internal sealed class StreamSlicFrameWriter : ISlicFrameWriter
    {
        private readonly ISingleStreamConnection _stream;

        public void Dispose()
        {
        }

        public async ValueTask WriteFrameAsync(
            SlicStream? stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            // A Slic frame must always be sent entirely even if the sending is canceled.
            ValueTask task = _stream.SendAsync(buffers, CancellationToken.None);
            if (task.IsCompleted || !cancel.CanBeCanceled)
            {
                await task.ConfigureAwait(false);
            }
            else
            {
                await task.AsTask().WaitAsync(cancel).ConfigureAwait(false);
            }
        }

        public ValueTask WriteStreamFrameAsync(
            SlicStream stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            // TODO: XXX, why does this fail?
            // Debug.Assert(buffers.Span[0].Span[..SlicDefinitions.FrameHeader.Length] == SlicDefinitions.FrameHeader.Span);

            int bufferSize = buffers.GetByteCount() - SlicDefinitions.FrameHeader.Length;

            // Compute how much space the size and stream ID require to figure out the start of the Slic
            // header.
            int streamIdLength = Ice20Encoder.GetSizeLength(stream.Id);
            bufferSize += streamIdLength;
            int sizeLength = Ice20Encoder.GetSizeLength(bufferSize);

            // Write the Slic frame header (frameType - byte, frameSize - varint, streamId - varlong). Since
            // we might not need the full space reserved for the header, we modify the send buffer to ensure
            // the first element points at the start of the Slic header. We'll restore the send buffer once
            // the send is complete (it's important for the tracing code which might rely on the encoded
            // data).
            ReadOnlyMemory<byte> previous = buffers.Span[0];
            Memory<byte> headerData = MemoryMarshal.AsMemory(buffers.Span[0]);
            headerData = headerData[(SlicDefinitions.FrameHeader.Length - sizeLength - streamIdLength - 1)..];

            headerData.Span[0] = (byte)(endStream ? FrameType.StreamLast : FrameType.Stream);
            Ice20Encoder.EncodeFixedLengthSize(bufferSize, headerData.Span.Slice(1, sizeLength));
            Ice20Encoder.EncodeFixedLengthSize(stream.Id, headerData.Span.Slice(1 + sizeLength, streamIdLength));

            // Update the first buffer entry
            MemoryMarshal.AsMemory(buffers).Span[0] = headerData;
            try
            {
                return WriteFrameAsync(stream, buffers, cancel);
            }
            finally
            {
                // Restore the original value of the send buffer.
                MemoryMarshal.AsMemory(buffers).Span[0] = previous;
            }
        }

        internal StreamSlicFrameWriter(ISingleStreamConnection stream) => _stream = stream;
    }
}
