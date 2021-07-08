// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;
using System.Buffers;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A stream writer to write a stream param to a <see cref="RpcStream"/>.</summary>
    public sealed class RpcStreamWriter
    {
        private readonly Func<RpcStream, Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>?, Task> _encoder;

        internal void Send(
            RpcStream stream,
            Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>? streamCompressor) =>
            Task.Run(() => _encoder(stream, streamCompressor));

        /// <summary>Creates a stream writer that writes the data from the given <see cref="System.IO.Stream"/> to the
        /// request <see cref="RpcStream"/>.</summary>
        /// <param name="byteStream">The stream to read data from.</param>
        public RpcStreamWriter(System.IO.Stream byteStream) =>
            _encoder = (stream, streamCompressor) => SendDataAsync(stream, streamCompressor, byteStream);

        static private async Task SendDataAsync(
            RpcStream rpcStream,
            Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>? streamCompressor,
            System.IO.Stream inputStream)
        {
            try
            {
                rpcStream.EnableSendFlowControl();

                // TODO: use a buffered stream to ensure the header isn't sent immediately?
                using System.IO.Stream ioStream = rpcStream.AsByteStream();

                // If there's a stream compressor, get the compression format and compressed output stream.
                CompressionFormat compressionFormat;
                System.IO.Stream outputStream;
                if (streamCompressor != null)
                {
                    (compressionFormat, outputStream) = streamCompressor(ioStream);
                }
                else
                {
                    (compressionFormat, outputStream) = (CompressionFormat.NotCompressed, ioStream);
                }

                // Write the unbounded data frame header.
                byte[] header = new byte[2];
                header[0] = (byte)Ice2FrameType.UnboundedData;
                header[1] = (byte)compressionFormat;
                await ioStream.WriteAsync(header).ConfigureAwait(false);

                try
                {
                    const int bufferSize = 81920;

                    // Write the data to the Rpc stream. We don't use Stream.CopyAsync here because we need to call
                    // FlushAsync on the output stream (in particular if the output stream is a compression stream).
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                    try
                    {
                        int bytesRead;
                        while ((bytesRead = await inputStream.ReadAsync(
                            new Memory<byte>(buffer)).ConfigureAwait(false)) != 0)
                        {
                            await outputStream.WriteAsync(
                                new ReadOnlyMemory<byte>(buffer, 0, bytesRead)).ConfigureAwait(false);

                            // TODO: should the frequency of the flush be configurable? When using compression and
                            // the input stream provides only small amount of data, we'll send many small compressed
                            // chunks of bytes.
                            await outputStream.FlushAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }

                    // Write end of stream (TODO: this might not work with Quic)
                    await ioStream.WriteAsync(Array.Empty<byte>()).ConfigureAwait(false);
                }
                catch
                {
                    rpcStream.AbortWrite(RpcStreamError.StreamingCanceledByWriter);
                    throw;
                }
            }
            finally
            {
                inputStream.Dispose();
            }
        }
    }
}
