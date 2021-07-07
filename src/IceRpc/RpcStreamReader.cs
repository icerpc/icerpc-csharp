// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System;
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

        /// <summary>Reads the stream data from an incoming request with a <see cref="System.IO.Stream"/>.</summary>
        /// <returns>The read-only <see cref="System.IO.Stream"/> to read the data from the request stream.</returns>
        static public System.IO.Stream ToByteStream(Dispatch dispatch) =>
            new StreamReaderIOStream(dispatch.IncomingRequest.Stream, dispatch.IncomingRequest.StreamDecompressor);

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
                            throw new InvalidDataException("cannot read compressed stream data");
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
    }
}
