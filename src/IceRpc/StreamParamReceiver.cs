// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace IceRpc
{
    /// <summary>A stream param receiver to receive stream param over a <see cref="RpcStream"/>.</summary>
    public sealed class StreamParamReceiver
    {
        private readonly RpcStream _stream;
        private readonly Func<CompressionFormat, System.IO.Stream, System.IO.Stream>? _streamDecompressor;

        /// <summary>Construct an <see cref="IAsyncEnumerable{T}"/> to receive the streamed param from an incoming
        /// request.</summary>
        /// <param name="dispatch">The request dispatch.</param>
        /// <param name="decodeAction">The action used to decode the streamed param.</param>
        /// <returns>The <see cref="IAsyncEnumerable{T}"/> to receive the streamed param.</returns>
        public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(Dispatch dispatch, Func<IceDecoder, T> decodeAction) =>
            new AsyncEnumerableStreamParamReceiver<T>(
                dispatch.IncomingRequest.Stream,
                dispatch.Connection,
                dispatch.ProxyInvoker,
                dispatch.Encoding,
                decodeAction).ReadAsync();

        /// <summary>Constructs a read-only <see cref="System.IO.Stream"/> to receive the streamed param from an
        /// incoming request.</summary>
        /// <returns>The read-only <see cref="System.IO.Stream"/> to receive the streamed param.</returns>
        public static System.IO.Stream ToByteStream(Dispatch dispatch) =>
            new ByteStreamParamReceiver(dispatch.IncomingRequest.Stream, dispatch.IncomingRequest.StreamDecompressor);

        /// <summary>Construct an <see cref="IAsyncEnumerable{T}"/> to receive the streamed param from an incoming
        /// response.</summary>
        /// <param name="connection">The connection used to construct the <see cref="IceDecoder"/>.</param>
        /// <param name="invoker">The invoker used to construct the <see cref="IceDecoder"/>.</param>
        /// <param name="encoding">The encoding.</param>
        /// <param name="decodeAction">The action used to decode the streamed params.</param>
        /// <remarks>This method is used to read element of fixed size that are stream with an
        /// <see cref="Ice2FrameType.UnboundedData"/> frame.</remarks>
        public IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            Connection connection,
            IInvoker? invoker,
            Encoding encoding,
            Func<IceDecoder, T> decodeAction) =>
            new AsyncEnumerableStreamParamReceiver<T>(_stream, connection, invoker, encoding, decodeAction).ReadAsync();

        /// <summary>Constructs a read-only <see cref="System.IO.Stream"/> to receive the streamed param from an
        /// incoming response.</summary>
        /// <returns>The read-only <see cref="System.IO.Stream"/> to receive the streamed param.</returns>
        public System.IO.Stream ToByteStream() => new ByteStreamParamReceiver(_stream, _streamDecompressor);

        internal StreamParamReceiver(
            RpcStream stream,
            Func<CompressionFormat, System.IO.Stream, System.IO.Stream>? streamDecompressor)
        {
            _stream = stream;
            _streamDecompressor = streamDecompressor;
        }

        private class ByteStreamParamReceiver : System.IO.Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotImplementedException();

            public override long Position
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }

            private System.IO.Stream? _ioStream;
            private readonly RpcStream _rpcStream;
            private readonly Func<CompressionFormat, System.IO.Stream, System.IO.Stream>? _streamDecompressor;

            public override void Flush() => throw new NotImplementedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                try
                {
                    return ReadAsync(buffer, offset, count, CancellationToken.None).Result;
                }
                catch (AggregateException ex)
                {
                    Debug.Assert(ex.InnerException != null);
                    throw ExceptionUtil.Throw(ex.InnerException);
                }
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
                ReadAsync(new Memory<byte>(buffer, offset, count), cancel).AsTask();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
            {
                if (_ioStream == null)
                {
                    // Receive the data frame header.
                    byte[] header = new byte[2];
                    await _rpcStream.ReceiveAsync(header, default).ConfigureAwait(false);
                    if (header[0] != (byte)Ice2FrameType.UnboundedData)
                    {
                        throw new InvalidDataException("invalid stream data");
                    }
                    var compressionFormat = (CompressionFormat)header[1];

                    // Read the unbounded data from the Rpc stream.
                    _ioStream = _rpcStream.AsByteStream();
                    if (compressionFormat != CompressionFormat.NotCompressed)
                    {
                        if (_streamDecompressor == null)
                        {
                            throw new NotSupportedException(
                                $"cannot decompress compression format '{compressionFormat}'");
                        }
                        else
                        {
                            _ioStream = _streamDecompressor(compressionFormat, _ioStream);
                        }
                    }
                }
                return await _ioStream.ReadAsync(buffer, cancel).ConfigureAwait(false);
            }

            public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotImplementedException();

            public override void SetLength(long value) => throw new NotImplementedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    _ioStream?.Dispose();
                    _rpcStream.AbortRead(RpcStreamError.StreamingCanceledByReader);
                }
            }

            internal ByteStreamParamReceiver(
                RpcStream stream,
                Func<CompressionFormat, System.IO.Stream, System.IO.Stream>? streamDecompressor)
            {
                _rpcStream = stream;
                _rpcStream.EnableReceiveFlowControl();
                _streamDecompressor = streamDecompressor;
            }
        }

        /// <summary>A stream reader to read variable size elements streamed in a <see cref="Ice2FrameType.BoundedData"/>
        /// frame into <see cref="IAsyncEnumerable{T}"/>.</summary>
        /// <typeparam name="T">The stream param type.</typeparam>
        private class AsyncEnumerableStreamParamReceiver<T>
        {
            private readonly Connection _connection;
            private readonly Func<IceDecoder, T> _decodeAction;
            private readonly Encoding _encoding;
            private readonly IInvoker? _invoker;
            private readonly RpcStream _rpcStream;

            internal AsyncEnumerableStreamParamReceiver(
                RpcStream rpcStream,
                Connection connection,
                IInvoker? invoker,
                Encoding encoding,
                Func<IceDecoder, T> decodeAction)
            {
                _rpcStream = rpcStream;
                _connection = connection;
                _invoker = invoker;
                _encoding = encoding;
                _decodeAction = decodeAction;
            }

            internal async IAsyncEnumerable<T> ReadAsync([EnumeratorCancellation] CancellationToken cancel = default)
            {
                cancel.Register(() => _rpcStream.AbortRead(RpcStreamError.StreamingCanceledByReader));

                while (true)
                {
                    // Receive the data frame header.
                    Memory<byte> buffer = new byte[256];
                    try
                    {
                        int received = await ReceiveFullAsync(buffer.Slice(0, 2), true, cancel).ConfigureAwait(false);
                        if (received == 0)
                        {
                            break; // EOF
                        }

                        if ((Ice2FrameType)buffer.Span[0] != Ice2FrameType.BoundedData)
                        {
                            throw new InvalidDataException(
                                $"invalid frame type '{buffer.Span[0]}' expected '{Ice2FrameType.BoundedData}'");
                        }

                        // Read the remainder of the size if needed.
                        int sizeLength = Ice20Decoder.DecodeSizeLength(buffer.Span[1]);
                        if (sizeLength > 1)
                        {
                            await ReceiveFullAsync(buffer.Slice(2, sizeLength - 1), false, cancel).ConfigureAwait(false);
                        }
                        int size = Ice20Decoder.DecodeSize(buffer[1..].AsReadOnlySpan()).Size;

                        if (size > _connection.IncomingFrameMaxSize)
                        {
                            throw new InvalidDataException(
                                @$"frame size of {size
                                } bytes is greater than the configured IncomingFrameMaxSize value ({
                                _connection.IncomingFrameMaxSize} bytes)");
                        }

                        buffer = size > buffer.Length ? new byte[size] : buffer.Slice(0, size);

                        await ReceiveFullAsync(buffer, false, cancel).ConfigureAwait(false);
                    }
                    catch
                    {
                        _rpcStream.AbortRead(RpcStreamError.StreamingCanceledByReader);
                        yield break; // finish iteration
                    }

                    var decoder = _encoding.CreateIceDecoder(buffer,
                                                             _connection,
                                                             _invoker,
                                                             _connection.Activator11);
                    T value = default!;
                    do
                    {
                        try
                        {
                            value = _decodeAction(decoder);
                        }
                        catch
                        {
                            _rpcStream.AbortRead(RpcStreamError.StreamingCanceledByReader);
                            yield break; // finish iteration
                        }
                        yield return value;
                    }
                    while (decoder.Pos < buffer.Length);
                }

                async ValueTask<int> ReceiveFullAsync(Memory<byte> buffer, bool checkEof, CancellationToken cancel)
                {
                    int offset = 0;
                    while (offset < buffer.Length)
                    {
                        int received = await _rpcStream.ReceiveAsync(buffer[offset..], cancel).ConfigureAwait(false);
                        if (received == 0)
                        {
                            if (checkEof && offset == 0)
                            {
                                return 0;
                            }
                            else
                            {
                                throw new InvalidDataException("unexpected end of stream");
                            }
                        }
                        offset += received;
                    }
                    return offset;
                }
            }
        }
    }
}
