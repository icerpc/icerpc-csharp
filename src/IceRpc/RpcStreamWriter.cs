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
        private readonly Action<RpcStream> _writer;

        internal void Send(RpcStream stream)
        {
            stream.EnableSendFlowControl();
            _writer(stream);
        }

        /// <summary>Creates a stream writer that writes the data from the given <see cref="System.IO.Stream"/> to the
        /// request <see cref="RpcStream"/>.</summary>
        /// <param name="byteStream">The stream to read data from.</param>
        public RpcStreamWriter(System.IO.Stream byteStream)
            : this(stream => Task.Run(() => SendData(stream, byteStream)))
        {
        }

        private RpcStreamWriter(Action<RpcStream> writer) => _writer = writer;

        static private async Task SendData(RpcStream stream, System.IO.Stream ioStream)
        {
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

            using IMemoryOwner<byte> receiveBufferOwner = MemoryPool<byte>.Shared.Rent(bufferSize);
            Memory<byte> receiveBuffer = receiveBufferOwner.Memory[0..bufferSize];
            var sendBuffers = new ReadOnlyMemory<byte>[1];
            int received;
            do
            {
                try
                {
                    stream.TransportHeader.CopyTo(receiveBuffer);
                    received = await ioStream.ReadAsync(receiveBuffer[stream.TransportHeader.Length..],
                                                        CancellationToken.None).ConfigureAwait(false);

                    sendBuffers[0] = receiveBuffer.Slice(0, stream.TransportHeader.Length + received);
                    await stream.SendAsync(sendBuffers,
                                           received == 0,
                                           CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    stream.AbortWrite(RpcStreamError.StreamingCanceledByWriter);
                    break;
                }
            }
            while (received > 0);

            ioStream.Dispose();
        }
    }
}
