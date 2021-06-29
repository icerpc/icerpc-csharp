// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A stream reader to read a stream param from a <see cref="RpcStream"/>.</summary>
    public sealed class RpcStreamReader
    {
        private readonly RpcStream _stream;

        /// <summary>Reads the stream data from the given dispatch's <see cref="RpcStream"/> with a
        /// <see cref="System.IO.Stream"/>.</summary>
        /// <returns>The read-only <see cref="System.IO.Stream"/> to read the data from the request stream.</returns>
        public static Stream ToByteStream(Dispatch dispatch)
        {
            dispatch.IncomingRequest.Stream.EnableReceiveFlowControl();
            return new RpcIOStream(dispatch.IncomingRequest.Stream);
        }

        /// <summary>Reads the stream data with a <see cref="System.IO.Stream"/>.</summary>
        /// <returns>The read-only <see cref="System.IO.Stream"/> to read the data from the request stream.</returns>
        public Stream ToByteStream()
        {
            _stream.EnableReceiveFlowControl();
            return new RpcIOStream(_stream);
        }

        internal RpcStreamReader(RpcStream stream) => _stream = stream;

        private class RpcIOStream : Stream
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
                catch(RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamError.StreamingCanceled)
                {
                    throw new IOException("streaming canceled");
                }
                catch(RpcStreamAbortedException ex)
                {
                    throw new IOException($"unexpected streaming error {ex.ErrorCode}");
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
                    _stream.AbortRead(RpcStreamError.StreamingCanceled);
                }
            }

            internal RpcIOStream(RpcStream stream) => _stream = stream;
        }
    }
}
