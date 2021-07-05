// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A stream writer to write a stream param to a <see cref="RpcStream"/>.</summary>
    public sealed class RpcStreamWriter
    {
        internal bool IsIOStreamData { get; }

        internal Func<System.IO.Stream, System.IO.Stream>? StreamCompressor { get; set; }

        private readonly Func<RpcStream, Task> _encoder;

        internal void Send(RpcStream stream)
        {
            stream.EnableSendFlowControl();
            Task.Run(() => _encoder(stream));
        }

        /// <summary>Creates a stream writer that writes the data from the given <see cref="System.IO.Stream"/> to the
        /// request <see cref="RpcStream"/>.</summary>
        /// <param name="byteStream">The stream to read data from.</param>
        public RpcStreamWriter(System.IO.Stream byteStream)
        {
            _encoder = stream => SendDataAsync(stream, byteStream);
            IsIOStreamData = true;
        }

        private async Task SendDataAsync(RpcStream stream, System.IO.Stream ioStream)
        {
            if (StreamCompressor != null)
            {
                ioStream = StreamCompressor(ioStream);
            }

            // We use the same default buffer size as System.IO.Stream.CopyToAsync()
            // TODO: Should this depend on the transport packet size? (Slic default packet size is 32KB for
            // example).
            int bufferSize = 81920;
            if (ioStream.CanSeek)
            {
                long remaining = ioStream.Length - ioStream.Position;
                if (remaining > 0)
                {
                    // Make sure there's enough space for the transport header
                    remaining += stream.TransportHeader.Length;

                    // In the case of a positive overflow, stick to the default size
                    bufferSize = (int)Math.Min(bufferSize, remaining);
                }
            }

            using IMemoryOwner<byte> bufferOwner = MemoryPool<byte>.Shared.Rent(bufferSize);
            Memory<byte> buffer = bufferOwner.Memory[0..bufferSize];
            var sendBuffers = new ReadOnlyMemory<byte>[1];
            int headerSize;
            int readSize;
            bool writeDataFrameHeader = true;
            do
            {
                try
                {
                    headerSize = stream.TransportHeader.Length;
                    stream.TransportHeader.CopyTo(buffer);
                    if (writeDataFrameHeader)
                    {
                        // For now the header is just a byte that indicates the compression format.
                        // TODO: Change the compression format to be a property of the writer?
                        buffer.Span[headerSize++] = StreamCompressor == null ?
                            (byte)CompressionFormat.NotCompressed : (byte)CompressionFormat.Deflate;
                        writeDataFrameHeader = false;
                    }

                    readSize = await ioStream.ReadAsync(buffer[bufferSize..],
                                                        CancellationToken.None).ConfigureAwait(false);

                    sendBuffers[0] = buffer.Slice(0, headerSize + readSize);
                    await stream.SendAsync(sendBuffers,
                                           readSize == 0,
                                           CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    stream.AbortWrite(RpcStreamError.StreamingCanceledByWriter);
                    break;
                }
            }
            while (readSize > 0);

            ioStream.Dispose();
        }
    }
}
