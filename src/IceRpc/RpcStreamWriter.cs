// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A stream writer to write stream params to a <see cref="RpcStream"/>.</summary>
    public interface IRpcStreamWriter
    {
        /// <summary>Writes the stream params to a <see cref="RpcStream"/>.</summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="streamCompressor">The compressor to apply to the encoded data.</param>
        void Send(RpcStream stream, Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>? streamCompressor);
    }

    /// <summary>A stream writer to write a unbounded stream params to a <see cref="RpcStream"/>.</summary>
    public sealed class RpcStreamWriter : IRpcStreamWriter
    {
        private readonly Func<RpcStream, Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>?, Task> _encoder;

        void IRpcStreamWriter.Send(
            RpcStream stream,
            Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>? streamCompressor) =>
            Task.Run(() => _encoder(stream, streamCompressor));

        /// <summary>Creates a stream writer that writes the data from the given <see cref="System.IO.Stream"/> to the
        /// request <see cref="RpcStream"/>.</summary>
        /// <param name="byteStream">The stream to read data from.</param>
        public RpcStreamWriter(System.IO.Stream byteStream) =>
            _encoder = (stream, streamCompressor) => SendDataAsync(stream, streamCompressor, byteStream);

        private static async Task SendDataAsync(
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

    /// <summary>A stream writer to write a unbounded stream params to a <see cref="RpcStream"/>.</summary>
    public sealed class UnboundedRpcStreamWriter<T> : IRpcStreamWriter
    {
        private readonly IAsyncEnumerable<T> _inputStream;
        private readonly Action<IceEncoder, T> _encodeAction;
        private readonly Encoding _encoding;
        private readonly Func<RpcStream, Task> _encoder;

        /// <summary>Creates a stream writer that writes the data from the given <see cref="IAsyncEnumerable{T}"/> to
        /// the request <see cref="RpcStream"/>.</summary>
        /// <param name="inputStream">The async enumerable to read the elements from.</param>
        /// <param name="encoding">The encoding.</param>
        /// <param name="encodeAction">The action to encode each element.</param>
        public UnboundedRpcStreamWriter(IAsyncEnumerable<T> inputStream, Encoding encoding, Action<IceEncoder, T> encodeAction)
        {
            _inputStream = inputStream;
            _encoding = encoding;
            _encodeAction = encodeAction;
            _encoder = stream => SendDataAsync(stream, _inputStream, _encoding, _encodeAction);
        }

        void IRpcStreamWriter.Send(
            RpcStream stream,
            Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>? streamCompressor) =>
            Task.Run(() => _encoder(stream));

        private static async Task SendDataAsync(
            RpcStream rpcStream,
            IAsyncEnumerable<T> inputStream,
            Encoding encoding,
            Action<IceEncoder, T> encodeAction)
        {
            rpcStream.EnableSendFlowControl();

            // TODO: use a buffered stream to ensure the header isn't sent immediately?
            using System.IO.Stream outputStream = rpcStream.AsByteStream();

            // Write the unbounded data frame header.
            byte[] header = new byte[2];
            header[0] = (byte)Ice2FrameType.UnboundedData;
            header[1] = (byte)CompressionFormat.NotCompressed;
            await outputStream.WriteAsync(header).ConfigureAwait(false);

            try
            {
                await foreach (T item in inputStream)
                {
                    var encoder = new IceEncoder(encoding);
                    encodeAction(encoder, item);
                    ReadOnlyMemory<ReadOnlyMemory<byte>> buffers = encoder.Finish();
                    for (int i = 0; i < buffers.Length; ++i)
                    {
                        await outputStream.WriteAsync(buffers.Span[i]).ConfigureAwait(false);
                    }
                    // TODO: should the frequency of the flush be configurable? When using compression and
                    // the input stream provides only small amount of data, we'll send many small compressed
                    // chunks of bytes.
                    await outputStream.FlushAsync().ConfigureAwait(false);
                }

                // Write end of stream (TODO: this might not work with Quic)
                await outputStream.WriteAsync(Array.Empty<byte>()).ConfigureAwait(false);
            }
            catch
            {
                rpcStream.AbortWrite(RpcStreamError.StreamingCanceledByWriter);
                throw;
            }
        }
    }

    /// <summary>A stream writer to write a unbounded stream params to a <see cref="RpcStream"/>.</summary>
    public sealed class BoundedRpcStreamWriter<T> : IRpcStreamWriter
    {
        private readonly IAsyncEnumerable<T> _inputStream;
        private readonly Action<IceEncoder, T> _encodeAction;
        private readonly Encoding _encoding;
        private readonly Func<RpcStream, Task> _encoder;

        /// <summary>Creates a stream writer that writes the data from the given <see cref="IAsyncEnumerable{T}"/> to
        /// the request <see cref="RpcStream"/>.</summary>
        /// <param name="inputStream">The async enumerable to read the elements from.</param>
        /// <param name="encoding">The encoding.</param>
        /// <param name="encodeAction">The action to encode each element.</param>
        public BoundedRpcStreamWriter(IAsyncEnumerable<T> inputStream, Encoding encoding, Action<IceEncoder, T> encodeAction)
        {
            _inputStream = inputStream;
            _encoding = encoding;
            _encodeAction = encodeAction;
            _encoder = stream => SendDataAsync(stream, _inputStream, _encoding, _encodeAction);
        }

        void IRpcStreamWriter.Send(
            RpcStream stream,
            Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>? streamCompressor) =>
            Task.Run(() => _encoder(stream));

        private static async Task SendDataAsync(
            RpcStream rpcStream,
            IAsyncEnumerable<T> inputStream,
            Encoding encoding,
            Action<IceEncoder, T> encodeAction)
        {
            rpcStream.EnableSendFlowControl();

            // TODO: use a buffered stream to ensure the header isn't sent immediately?
            using System.IO.Stream outputStream = rpcStream.AsByteStream();
            try
            {
                await foreach (T item in inputStream)
                {
                    var encoder = new IceEncoder(encoding);
                    encoder.EncodeByte((byte)Ice2FrameType.BoundedData);
                    IceEncoder.Position start = encoder.StartFixedLengthSize();
                    encodeAction(encoder, item);
                    encoder.EndFixedLengthSize(start);
                    ReadOnlyMemory<ReadOnlyMemory<byte>> buffers = encoder.Finish();
                    for (int i = 0; i < buffers.Length; ++i)
                    {
                        await outputStream.WriteAsync(buffers.Span[i]).ConfigureAwait(false);
                    }
                    // TODO: should the frequency of the flush be configurable? When using compression and
                    // the input stream provides only small amount of data, we'll send many small compressed
                    // chunks of bytes.
                    await outputStream.FlushAsync().ConfigureAwait(false);
                }
                // Write end of stream (TODO: this might not work with Quic)
                await outputStream.WriteAsync(Array.Empty<byte>()).ConfigureAwait(false);
            }
            catch
            {
                rpcStream.AbortWrite(RpcStreamError.StreamingCanceledByWriter);
                throw;
            }
        }
    }
}
