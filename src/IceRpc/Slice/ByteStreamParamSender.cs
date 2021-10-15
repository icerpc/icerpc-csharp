// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System.Buffers;

namespace IceRpc.Slice
{
    /// <summary>A stream param sender that encapsulates a <see cref="System.IO.Stream"/> and it is used to send
    /// <c>stream byte</c> params using a <see cref="Ice2FrameType.UnboundedData"/> frame.</summary>
    public sealed class ByteStreamParamSender : IStreamParamSender
    {
        private readonly Func<IMultiplexedNetworkStream, Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>?, Task> _encoder;

        Task IStreamParamSender.SendAsync(
            IMultiplexedNetworkStream stream,
            Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>? streamCompressor) =>
            _encoder(stream, streamCompressor);

        /// <summary>Constructs a byte stream param sender from the given <see cref="System.IO.Stream"/>.</summary>
        /// <param name="byteStream">The stream to read from.</param>
        public ByteStreamParamSender(System.IO.Stream byteStream) =>
            _encoder = (stream, streamCompressor) => SendAsync(stream, streamCompressor, byteStream);

        private static async Task SendAsync(
            IMultiplexedNetworkStream rpcStream,
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
                    rpcStream.AbortWrite(StreamError.StreamingCanceledByWriter);
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
