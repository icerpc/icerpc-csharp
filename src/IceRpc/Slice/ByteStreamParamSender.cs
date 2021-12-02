// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System.Buffers;

namespace IceRpc.Slice
{
    /// <summary>A stream param sender that encapsulates a <see cref="System.IO.Stream"/> and it is used to send
    /// <c>stream byte</c> params.</summary>
    public sealed class ByteStreamParamSender : IStreamParamSender
    {
        private readonly Func<IMultiplexedStream, Task> _encoder;

        Task IStreamParamSender.SendAsync(IMultiplexedStream stream) => _encoder(stream);

        /// <summary>Constructs a byte stream param sender from the given <see cref="System.IO.Stream"/>.</summary>
        /// <param name="byteStream">The stream to read from.</param>
        public ByteStreamParamSender(System.IO.Stream byteStream) =>
            _encoder = stream => SendAsync(stream, byteStream);

        private static async Task SendAsync(IMultiplexedStream multiplexedStream, System.IO.Stream inputStream)
        {
            try
            {
                using System.IO.Stream ioStream = multiplexedStream.AsByteStream();

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
                            await ioStream.WriteAsync(
                                new ReadOnlyMemory<byte>(buffer, 0, bytesRead)).ConfigureAwait(false);

                            // TODO: should the frequency of the flush be configurable? When using compression and
                            // the input stream provides only small amount of data, we'll send many small compressed
                            // chunks of bytes.
                            await ioStream.FlushAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }

                    // Write end of stream (TODO: this might not work with Quic)
                    await ioStream.WriteAsync(Array.Empty<byte>()).ConfigureAwait(false);
                }
                catch (MultiplexedStreamAbortedException ex) when (
                    ex.ErrorCode == (byte)MultiplexedStreamError.StreamingCanceledByReader)
                {
                    throw new IOException("streaming canceled by the reader", ex);
                }
                catch (MultiplexedStreamAbortedException ex)
                {
                    throw new IOException($"unexpected streaming error {ex.ErrorCode}", ex);
                }
                catch
                {
                    multiplexedStream.AbortWrite((byte)MultiplexedStreamError.StreamingCanceledByWriter);
                    throw;
                }
            }
            finally
            {
                await inputStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
