// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Runtime.InteropServices;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Slic frame writer class writes Slic frames.</summary>
    internal sealed class SlicFrameWriter : ISlicFrameWriter
    {
        private readonly Func<ReadOnlyMemory<ReadOnlyMemory<byte>>, CancellationToken, ValueTask> _writeFunc;

        public async ValueTask WriteFrameAsync(
            SlicMultiplexedStream? stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            // A Slic frame must always be sent entirely even if the sending is canceled.
            ValueTask task = _writeFunc(buffers, CancellationToken.None);
            if (task.IsCompleted || !cancel.CanBeCanceled)
            {
                await task.ConfigureAwait(false);
            }
            else
            {
                await task.AsTask().WaitAsync(cancel).ConfigureAwait(false);
            }
        }

        public async ValueTask WriteStreamFrameAsync(
            SlicMultiplexedStream stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            int bufferSize = buffers.GetByteCount() - SlicDefinitions.FrameHeader.Length;

            // Compute how much space the size and stream ID require to figure out the start of the Slic
            // header.
            int streamIdLength = SliceEncoder.GetVarLongEncodedSize(stream.Id);
            bufferSize += streamIdLength;
            int sizeLength = Slice20Encoding.GetSizeLength(bufferSize);

            // Write the Slic frame header (frameType as a byte, frameSize as a varint, streamId as a
            // varulong). Since we might not need the full space reserved for the header, we modify the send
            // buffer to ensure the first element points at the start of the Slic header. We'll restore the
            // send buffer once the send is complete (it's important for the tracing code which might rely on
            // the encoded data).
            Memory<byte> headerData = MemoryMarshal.AsMemory(buffers.Span[0]);
            headerData = headerData[(SlicDefinitions.FrameHeader.Length - sizeLength - streamIdLength - 1)..];
            headerData.Span[0] = (byte)(endStream ? FrameType.StreamLast : FrameType.Stream);
            Slice20Encoding.EncodeSize(bufferSize, headerData.Span.Slice(1, sizeLength));
            // TODO: is stream.Id a long or a ulong?
            SliceEncoder.EncodeVarULong(
                checked((ulong)stream.Id),
                headerData.Span.Slice(1 + sizeLength, streamIdLength));
            MemoryMarshal.AsMemory(buffers).Span[0] = headerData;

            await WriteFrameAsync(stream, buffers, cancel).ConfigureAwait(false);
        }

        internal SlicFrameWriter(Func<ReadOnlyMemory<ReadOnlyMemory<byte>>, CancellationToken, ValueTask> writeFunc) =>
            _writeFunc = writeFunc;
    }
}
