// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A stream reader to read a stream param from a <see cref="RpcStream"/>.</summary>
    public sealed class RpcStreamReader
    {
        private readonly RpcStream _stream;
        private readonly Func<CompressionFormat, System.IO.Stream, System.IO.Stream>? _streamDecompressor;

        /// <summary>Reads the stream data from an incoming request with a <see cref="IAsyncEnumerable{T}"/>.</summary>
        /// <param name="dispatch">The request dispatch.</param>
        /// <param name="decodeAction">The action used to decode the stream param.</param>
        /// <param name="elementSize">The size in bytes of the stream element.</param>
        /// <remarks>This method is used to read element of fixed size that are stream with an
        /// <see cref="Ice2FrameType.UnboundedData"/> frame.</remarks>
        public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            Dispatch dispatch,
            Func<IceDecoder, T> decodeAction,
            int elementSize) =>
            new StreamReaderUnboundedEnumerable<T>(
                dispatch.IncomingRequest.Stream,
                dispatch.Encoding,
                decodeAction,
                elementSize).ReadAsync();

        /// <summary>Reads the stream data from an incoming request with a <see cref="IAsyncEnumerable{T}"/>.</summary>
        /// <param name="dispatch">The request dispatch.</param>
        /// <param name="decodeAction">The action used to decode the stream param.</param>
        /// <remarks>This method is used to read element of variable size that are stream with an
        /// <see cref="Ice2FrameType.BoundedData"/> frame.</remarks>
        public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(Dispatch dispatch, Func<IceDecoder, T> decodeAction) =>
            new StreamReaderBoundedEnumerable<T>(
                dispatch.IncomingRequest.Stream,
                dispatch.Connection,
                dispatch.ProxyInvoker,
                dispatch.Encoding,
                decodeAction).ReadAsync();

        /// <summary>Reads the stream data from an incoming request with a <see cref="System.IO.Stream"/>.</summary>
        /// <returns>The read-only <see cref="System.IO.Stream"/> to read the data from the request stream.</returns>
        public static System.IO.Stream ToByteStream(Dispatch dispatch) =>
            new StreamReaderIOStream(dispatch.IncomingRequest.Stream, dispatch.IncomingRequest.StreamDecompressor);

        /// <summary>Reads the stream data from an outgoing request with a <see cref="IAsyncEnumerable{T}"/>.</summary>
        /// <param name="encoding">The encoding.</param>
        /// <param name="decodeAction">The action used to decode the stream param.</param>
        /// <param name="elementSize">The size in bytes of the stream element.</param>
        /// <remarks>This method is used to read element of fixed size that are stream with an
        /// <see cref="Ice2FrameType.UnboundedData"/> frame.</remarks>
        public IAsyncEnumerable<T> ToAsyncEnumerable<T>(Encoding encoding, Func<IceDecoder, T> decodeAction, int elementSize) =>
            new StreamReaderUnboundedEnumerable<T>(_stream, encoding, decodeAction, elementSize).ReadAsync();

        /// <summary>Reads the stream data from an outgoing request with a <see cref="IAsyncEnumerable{T}"/>.</summary>
        /// <param name="connection">The connection used to construct the <see cref="IceDecoder"/>.</param>
        /// <param name="invoker">The invoker used to construct the <see cref="IceDecoder"/>.</param>
        /// <param name="encoding">The encoding.</param>
        /// <param name="decodeAction">The action used to decode the stream param.</param>
        /// <remarks>This method is used to read element of fixed size that are stream with an
        /// <see cref="Ice2FrameType.UnboundedData"/> frame.</remarks>
        public IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            Connection connection,
            IInvoker? invoker,
            Encoding encoding,
            Func<IceDecoder, T> decodeAction) =>
            new StreamReaderBoundedEnumerable<T>(_stream, connection, invoker, encoding, decodeAction).ReadAsync();

        /// <summary>Reads the stream data from an outgoing request with a <see cref="System.IO.Stream"/>.</summary>
        /// <returns>The read-only <see cref="System.IO.Stream"/> to read the data from the request stream.</returns>
        public System.IO.Stream ToByteStream() => new StreamReaderIOStream(_stream, _streamDecompressor);

        /// <summary>Constructs a stream reader to read a stream param from an outgoing request.</summary>
        public RpcStreamReader(OutgoingRequest request)
            : this(request.Stream, request.StreamDecompressor)
        {
        }

        internal RpcStreamReader(
            RpcStream stream,
            Func<CompressionFormat, System.IO.Stream, System.IO.Stream>? streamDecompressor)
        {
            _stream = stream;
            _streamDecompressor = streamDecompressor;
        }

        private class StreamReaderIOStream : System.IO.Stream
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

            internal StreamReaderIOStream(
                RpcStream stream,
                Func<CompressionFormat, System.IO.Stream, System.IO.Stream>? streamDecompressor)
            {
                _rpcStream = stream;
                _rpcStream.EnableReceiveFlowControl();
                _streamDecompressor = streamDecompressor;
            }
        }

        /// <summary>A stream reader to read fixed size elements streamed in a <see cref="Ice2FrameType.UnboundedData"/>
        /// frame into <see cref="IAsyncEnumerable{T}"/>.</summary>
        /// <typeparam name="T">The stream param type.</typeparam>
        private class StreamReaderUnboundedEnumerable<T>
        {
            private readonly Func<IceDecoder, T> _decodeAction;
            private readonly int _elementSize;
            private readonly Encoding _encoding;
            private readonly RpcStream _rpcStream;

            internal StreamReaderUnboundedEnumerable(
                RpcStream rpcStream,
                Encoding encoding,
                Func<IceDecoder, T> decodeAction,
                int elementSize)
            {
                _rpcStream = rpcStream;
                _encoding = encoding;
                _decodeAction = decodeAction;
                _elementSize = elementSize;
            }

            internal async IAsyncEnumerable<T> ReadAsync()
            {
                // Receive the data frame header.
                byte[] header = new byte[2];
                await _rpcStream.ReceiveAsync(header, default).ConfigureAwait(false);
                if (header[0] != (byte)Ice2FrameType.UnboundedData)
                {
                    throw new InvalidDataException("invalid stream data");
                }

                byte[] buffer = new byte[_elementSize];

                while (true)
                {
                    int received = await ReceiveFullAsync(buffer.AsMemory()).ConfigureAwait(false);
                    if (received == 0)
                    {
                        break; // EOF
                    }
                    yield return _decodeAction(new IceDecoder(buffer, _encoding));
                }

                async ValueTask<int> ReceiveFullAsync(Memory<byte> buffer)
                {
                    int offset = 0;
                    while (offset < buffer.Length)
                    {
                        int received = await _rpcStream.ReceiveAsync(buffer[offset..], default).ConfigureAwait(false);
                        if (received == 0)
                        {
                            if (offset == 0)
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
                    return buffer.Length;
                }
            }
        }

        /// <summary>A stream reader to read variable size elements streamed in a <see cref="Ice2FrameType.BoundedData"/>
        /// frame into <see cref="IAsyncEnumerable{T}"/>.</summary>
        /// <typeparam name="T">The stream param type.</typeparam>
        private class StreamReaderBoundedEnumerable<T>
        {
            private readonly Connection _connection;
            private readonly Func<IceDecoder, T> _decodeAction;
            private readonly Encoding _encoding;
            private readonly IInvoker? _invoker;
            private readonly RpcStream _rpcStream;

            internal StreamReaderBoundedEnumerable(
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

            internal async IAsyncEnumerable<T> ReadAsync()
            {
                while (true)
                {
                    // Receive the data frame header.
                    byte[] header = new byte[5];
                    int received = await ReceiveFullAsync(header).ConfigureAwait(false);
                    if (received == 0)
                    {
                        break; // EOF
                    }
                    if (header[0] != (byte)Ice2FrameType.BoundedData)
                    {
                        throw new InvalidDataException("invalid stream data");
                    }

                    (ulong elementSize, int _) = ByteBuffer.DecodeVarULong(header[1..].AsSpan());

                    byte[] buffer = new byte[elementSize];

                    received = await ReceiveFullAsync(buffer.AsMemory()).ConfigureAwait(false);
                    if (received == 0)
                    {
                        break; // EOF
                    }
                    yield return _decodeAction(
                        new IceDecoder(buffer, _encoding, _connection, _invoker, _connection.ClassFactory));
                }

                async ValueTask<int> ReceiveFullAsync(Memory<byte> buffer)
                {
                    int offset = 0;
                    while (offset < buffer.Length)
                    {
                        int received = await _rpcStream.ReceiveAsync(buffer[offset..], default).ConfigureAwait(false);
                        if (received == 0)
                        {
                            if (offset == 0)
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
                    return buffer.Length;
                }
            }
        }
    }
}
