// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace IceRpc.Slice
{
    /// <summary>A stream param receiver to receive stream param over an <see cref="INetworkStream"/>.</summary>
    public sealed class StreamParamReceiver
    {
        private readonly INetworkStream _stream;
        private readonly Func<CompressionFormat, System.IO.Stream, System.IO.Stream>? _streamDecompressor;

        /// <summary>Construct an <see cref="IAsyncEnumerable{T}"/> to receive the streamed param from an incoming
        /// request.</summary>
        /// <param name="request">The incoming request.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="decodeAction">The action used to decode the streamed param.</param>
        /// <returns>The <see cref="IAsyncEnumerable{T}"/> to receive the streamed param.</returns>
        public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            IncomingRequest request,
            IIceDecoderFactory<IceDecoder> iceDecoderFactory,
            Func<IceDecoder, T> decodeAction) =>
            new AsyncEnumerableStreamParamReceiver<T>(
                request.Stream,
                request.Connection,
                request.ProxyInvoker,
                iceDecoderFactory,
                decodeAction).ReadAsync();

        /// <summary>Constructs a read-only <see cref="System.IO.Stream"/> to receive the streamed param from an
        /// incoming request.</summary>
        /// <returns>The read-only <see cref="System.IO.Stream"/> to receive the streamed param.</returns>
        public static System.IO.Stream ToByteStream(IncomingRequest request) =>
            new ByteStreamParamReceiver(request.Stream, request.StreamDecompressor);

        /// <summary>Construct an <see cref="IAsyncEnumerable{T}"/> to receive the streamed param from an incoming
        /// response.</summary>
        /// <param name="response">The incoming response.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="iceDecoderFactory">The Ice decoder factory.</param>
        /// <param name="decodeAction">The action used to decode the streamed params.</param>
        /// <remarks>This method is used to read element of fixed size that are stream with an
        /// <see cref="Ice2FrameType.UnboundedData"/> frame.</remarks>
        public IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            IncomingResponse response,
            IInvoker? invoker,
            IIceDecoderFactory<IceDecoder> iceDecoderFactory,
            Func<IceDecoder, T> decodeAction) =>
            new AsyncEnumerableStreamParamReceiver<T>(
                _stream,
                response.Connection,
                invoker,
                iceDecoderFactory,
                decodeAction).ReadAsync();

        /// <summary>Constructs a read-only <see cref="System.IO.Stream"/> to receive the streamed param from an
        /// incoming response.</summary>
        /// <returns>The read-only <see cref="System.IO.Stream"/> to receive the streamed param.</returns>
        public System.IO.Stream ToByteStream() => new ByteStreamParamReceiver(_stream, _streamDecompressor);

        internal StreamParamReceiver(
            INetworkStream stream,
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
            private readonly INetworkStream? _networkStream;
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
                if (_networkStream == null)
                {
                    return 0;
                }

                if (_ioStream == null)
                {
                    // Receive the data frame header.
                    byte[] header = new byte[2];
                    await _networkStream.ReadAsync(header, default).ConfigureAwait(false);
                    if (header[0] != (byte)Ice2FrameType.UnboundedData)
                    {
                        throw new InvalidDataException("invalid stream data");
                    }
                    var compressionFormat = (CompressionFormat)header[1];

                    // Read the unbounded data from the Rpc stream.
                    _ioStream = _networkStream.AsByteStream();
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
                    _networkStream?.AbortRead(StreamError.StreamingCanceledByReader);
                }
            }

            internal ByteStreamParamReceiver(
                INetworkStream? stream,
                Func<CompressionFormat, System.IO.Stream, System.IO.Stream>? streamDecompressor)
            {
                _networkStream = stream;
                _networkStream?.EnableReceiveFlowControl();
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
            private readonly IIceDecoderFactory<IceDecoder> _decoderFactory;
            private readonly IInvoker? _invoker;
            private readonly INetworkStream? _networkStream;

            internal AsyncEnumerableStreamParamReceiver(
                INetworkStream? networkStream,
                Connection connection,
                IInvoker? invoker,
                IIceDecoderFactory<IceDecoder> decoderFactory,
                Func<IceDecoder, T> decodeAction)
            {
                _networkStream = networkStream;
                _networkStream?.EnableReceiveFlowControl();
                _connection = connection;
                _invoker = invoker;
                _decoderFactory = decoderFactory;
                _decodeAction = decodeAction;
            }

            internal async IAsyncEnumerable<T> ReadAsync([EnumeratorCancellation] CancellationToken cancel = default)
            {
                if (_networkStream == null)
                {
                    yield break; // finish iteration
                }

                cancel.Register(() => _networkStream.AbortRead(StreamError.StreamingCanceledByReader));

                while (true)
                {
                    // Receive the data frame header.
                    Memory<byte> buffer = new byte[256];
                    try
                    {
                        // TODO: Use Ice2 protocol frame reader to read the frame
                        int received = await _networkStream.ReadAsync(buffer[0..2], cancel).ConfigureAwait(false);
                        if (received == 0)
                        {
                            break; // EOF
                        }
                        else if (received == 1)
                        {
                            await _networkStream.ReceiveUntilFullAsync(buffer[1..2], cancel).ConfigureAwait(false);
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
                            await _networkStream.ReceiveUntilFullAsync(
                                buffer.Slice(2, sizeLength - 1),
                                cancel).ConfigureAwait(false);
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

                        await _networkStream.ReceiveUntilFullAsync(buffer, cancel).ConfigureAwait(false);
                    }
                    catch
                    {
                        _networkStream.AbortRead(StreamError.StreamingCanceledByReader);
                        yield break; // finish iteration
                    }

                    IceDecoder decoder = _decoderFactory.CreateIceDecoder(buffer, _connection, _invoker);
                    T value = default!;
                    do
                    {
                        try
                        {
                            value = _decodeAction(decoder);
                        }
                        catch
                        {
                            _networkStream.AbortRead(StreamError.StreamingCanceledByReader);
                            yield break; // finish iteration
                        }
                        yield return value;
                    }
                    while (decoder.Pos < buffer.Length);
                }
            }
        }
    }
}
