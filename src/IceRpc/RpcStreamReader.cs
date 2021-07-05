// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A stream reader to read a stream param from a <see cref="RpcStream"/>.</summary>
    public sealed class RpcStreamReader
    {
        private readonly OutgoingRequest _request;

        /// <summary>Reads the stream data from the given incoming request's <see cref="RpcStream"/> with a
        /// <see cref="Stream"/>.</summary>
        /// <returns>The read-only <see cref="Stream"/> to read the data from the request stream.</returns>
        public static Stream ToByteStream(IncomingRequest request)
        {
            request.Stream.EnableReceiveFlowControl();
            return new ByteStream(request.Stream);
        }

        /// <summary>Reads the stream data with a <see cref="Stream"/>.</summary>
        /// <returns>The read-only <see cref="Stream"/> to read the data from the request stream.</returns>
        public Stream ToByteStream()
        {
            _request.Stream.EnableReceiveFlowControl();
            return new ByteStream(_request.Stream);
        }

        internal RpcStreamReader(OutgoingRequest request) => _request = request;

        private class ByteStream : Stream
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

            private readonly RpcStream _stream;

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
                try
                {
                    return await _stream.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
                }
                catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamError.StreamingCanceledByWriter)
                {
                    throw new IOException("streaming canceled by the writer", ex);
                }
                catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamError.StreamingCanceledByReader)
                {
                    throw new IOException("streaming canceled by the reader", ex);
                }
                catch (RpcStreamAbortedException ex)
                {
                    throw new IOException($"unexpected streaming error {ex.ErrorCode}", ex);
                }
                catch (Exception ex)
                {
                    throw new IOException($"unexpected exception", ex);
                }
            }

            public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotImplementedException();
            public override void SetLength(long value) => throw new NotImplementedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    _stream.AbortRead(RpcStreamError.StreamingCanceledByReader);
                }
            }

            internal ByteStream(RpcStream stream) => _stream = stream;
        }
    }
}
