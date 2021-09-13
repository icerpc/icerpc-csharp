// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports.Internal;
using System.Diagnostics;

namespace IceRpc.Transports
{
    /// <summary>Raised if a stream is aborted. This exception is internal.</summary>
    public class RpcStreamAbortedException : Exception
    {
        internal RpcStreamError ErrorCode { get; }

        internal RpcStreamAbortedException(RpcStreamError errorCode) :
            base($"stream aborted with error code {errorCode}") => ErrorCode = errorCode;
    }

    /// <summary>Error codes for stream errors.</summary>
    public enum RpcStreamError : byte
    {
        /// <summary>The stream was aborted because the invocation was canceled.</summary>
        InvocationCanceled,

        /// <summary>The stream was aborted because the dispatch was canceled.</summary>
        DispatchCanceled,

        /// <summary>Streaming was canceled by the reader.</summary>
        StreamingCanceledByReader,

        /// <summary>Streaming was canceled by the writer.</summary>
        StreamingCanceledByWriter,

        /// <summary>The stream was aborted because the connection was shutdown.</summary>
        ConnectionShutdown,

        /// <summary>The stream was aborted because the connection was shutdown by the peer.</summary>
        ConnectionShutdownByPeer,

        /// <summary>The stream was aborted because the connection was aborted.</summary>
        ConnectionAborted,

        /// <summary>Stream data is not expected.</summary>
        UnexpectedStreamData,

        /// <summary>The stream was aborted.</summary>
        StreamAborted
    }

    /// <summary>The NetworkStream abstract base class to be overridden by multi-stream network connection
    /// implementations. There's an instance of this class for each active stream managed by the multi-stream
    /// network connection.</summary>
    public abstract class RpcStream
    {
        /// <summary>The stream ID. If the stream ID hasn't been assigned yet, an exception is thrown. Assigning the
        /// stream ID registers the stream with the connection.</summary>
        /// <exception cref="InvalidOperationException">If the stream ID has not been assigned yet.</exception>
        public long Id
        {
            get
            {
                if (_id == -1)
                {
                    throw new InvalidOperationException("stream ID isn't allocated yet");
                }
                return _id;
            }
            set
            {
                Debug.Assert(_id == -1);
                // First add the stream and then assign the ID. AddStream can throw if the connection is closed and
                // in this case we want to make sure the id isn't assigned since the stream isn't considered
                // allocated if not added to the connection.
                _connection.AddStream(value, this, ref _id);
            }
        }

        /// <summary>Returns <c>true</c> if the stream is an incoming stream, <c>false</c> otherwise.</summary>
        public bool IsIncoming => _id != -1 && _id % 2 == (_connection.IsServer ? 0 : 1);

        /// <summary>Returns <c>true</c> if the stream is a bidirectional stream, <c>false</c> otherwise.</summary>
        public bool IsBidirectional { get; }

        /// <summary>Returns <c>true</c> if the stream is shutdown, <c>false</c> otherwise.</summary>
        public bool IsShutdown => (Thread.VolatileRead(ref _state) & (int)State.Shutdown) > 0;

        /// <summary>Returns <c>true</c> if the receiving side of the stream is completed, <c>false</c> otherwise.
        /// </summary>
        public bool ReadCompleted => (Thread.VolatileRead(ref _state) & (int)State.ReadCompleted) > 0;

        /// <summary>Returns <c>true</c> if the sending side of the stream is completed, <c>false</c> otherwise.
        /// </summary>
        public bool WriteCompleted => (Thread.VolatileRead(ref _state) & (int)State.WriteCompleted) > 0;

        /// <summary>The transport header sentinel. Transport implementations that need to add an additional header
        /// to transmit data over the stream can provide the header data here. This can improve performance by reducing
        /// the number of allocations as Ice will allocate buffer space for both the transport header and the Ice
        /// protocol header. If a header is returned here, the implementation of the SendAsync method should expect
        /// this header to be set at the start of the first buffer.</summary>
        public virtual ReadOnlyMemory<byte> TransportHeader => default;

        /// <summary>Get the cancellation dispatch source.</summary>
        internal CancellationTokenSource? CancelDispatchSource { get; }

        /// <summary>Returns true if the stream ID is assigned</summary>
        internal bool IsStarted => _id != -1;

        // TODO: remove when we implement Ice1 with the protocol handler
        private int RequestId => IsBidirectional ? ((int)(Id >> 2) + 1) : 0;

        private readonly MultiStreamConnection _connection;

        // Depending on the stream implementation, the _id can be assigned on construction or only once SendAsync
        // is called. Once it's assigned, it's immutable. The specialization of the stream is responsible for not
        // accessing this data member concurrently when it's not safe.
        private long _id = -1;

        private int _state;

        /// <summary>Abort the stream read side.</summary>
        /// <param name="errorCode">The reason of the abort.</param>
        public abstract void AbortRead(RpcStreamError errorCode);

        /// <summary>Abort the stream write side.</summary>
        /// <param name="errorCode">The reason of the abort.</param>
        public abstract void AbortWrite(RpcStreamError errorCode);

        /// <summary>Get a <see cref="System.IO.Stream"/> to allow using this stream using the C# stream API.</summary>
        /// <returns>The <see cref="System.IO.Stream"/> object.</returns>
        public virtual System.IO.Stream AsByteStream() => new ByteStream(this);

        /// <summary>Enable flow control for receiving data from the peer over the stream. This is called after
        /// receiving a request or response frame to receive data for a stream parameter. Flow control isn't
        /// enabled for receiving the request or response frame whose size is limited with IncomingFrameSizeMax.
        /// The stream relies on the underlying transport flow control instead (TCP, Quic, ...). For stream
        /// parameters, whose size is not limited, it's important that the transport doesn't send an unlimited
        /// amount of data if the receiver doesn't process it. For TCP based transports, this would cause the
        /// send buffer to fill up and this would prevent other streams to be processed.</summary>
        public abstract void EnableReceiveFlowControl();

        /// <summary>Enable flow control for sending data to the peer over the stream. This is called after
        /// sending a request or response frame to send data from a stream parameter.</summary>
        public abstract void EnableSendFlowControl();

        /// <summary>Receives data in the given buffer and return the number of received bytes.</summary>
        /// <param name="buffer">The buffer to store the received data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The number of bytes received.</return>
        public abstract ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Sends data from the given buffers and returns once the buffers are sent.</summary>
        /// <param name="buffers">The buffers with the data to send.</param>
        /// <param name="endStream">True if no more data will be sent over this stream, False otherwise.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract ValueTask SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel);

        /// <inheritdoc/>
        public override string ToString() => $"{base.ToString()} (ID={Id})";

        /// <summary>Constructs a stream with the given ID.</summary>
        /// <param name="streamId">The stream ID.</param>
        /// <param name="connection">The parent connection.</param>
        protected RpcStream(MultiStreamConnection connection, long streamId)
        {
            _connection = connection;
            IsBidirectional = streamId % 4 < 2;
            _connection.AddStream(streamId, this, ref _id);

            if (IsBidirectional && IsIncoming)
            {
                CancelDispatchSource = new CancellationTokenSource();
            }
            else
            {
                // Write-side or read-size of unidirectional stream is marked as completed.
                if (IsIncoming)
                {
                    TrySetWriteCompleted();
                }
                else
                {
                    TrySetReadCompleted();
                }
            }
        }

        /// <summary>Constructs an outgoing stream.</summary>
        /// <param name="bidirectional"><c>true</c> to create a bidirectional stream, <c>false</c> otherwise.</param>
        /// <param name="connection">The parent connection.</param>
        protected RpcStream(MultiStreamConnection connection, bool bidirectional)
        {
            _connection = connection;
            IsBidirectional = bidirectional;
            if (!IsBidirectional)
            {
                // Read-side of outgoing unidirectional stream is marked as completed.
                TrySetReadCompleted();
            }
        }

        /// <summary>Shutdown the stream. This is called when the stream read and write sides are completed.</summary>
        protected virtual void Shutdown()
        {
            Debug.Assert(_state == (int)(State.ReadCompleted | State.WriteCompleted | State.Shutdown));

            if (CancelDispatchSource is CancellationTokenSource source)
            {
                // Cancel the dispatch.
                source.Cancel();

                // We're done with the source, dispose it.
                source.Dispose();
            }
            _connection.RemoveStream(Id);
        }

        /// <summary>Mark reads as completed for this stream.</summary>
        /// <returns><c>true</c> if the stream reads were successfully marked as completed, <c>false</c> if the stream
        /// reads were already completed.</returns>
        protected internal bool TrySetReadCompleted(bool shutdown = true) =>
            TrySetState(State.ReadCompleted, shutdown);

        /// <summary>Mark writes as completed for this stream.</summary>
        /// <returns><c>true</c> if the stream writes were successfully marked as completed, <c>false</c> if the stream
        /// writes were already completed.</returns>
        protected internal bool TrySetWriteCompleted(bool shutdown = true) =>
            TrySetState(State.WriteCompleted, shutdown);

        /// <summary>Shutdown the stream if it's not already shutdown.</summary>
        protected void TryShutdown()
        {
            // If both reads and writes are completed, the stream is started and not already shutdown, call shutdown.
            if (ReadCompleted && WriteCompleted && TrySetState(State.Shutdown, false) && IsStarted)
            {
                try
                {
                    Shutdown();
                }
                catch (Exception exception)
                {
                    Debug.Assert(false, $"unexpected exception {exception}");
                }
            }
        }

        internal void Abort(RpcStreamError errorCode)
        {
            // Abort writes.
            AbortWrite(errorCode);

            // Abort reads.
            AbortRead(errorCode);
        }

        internal async ValueTask ReceiveFullAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            // Loop until we received enough data to fully fill the given buffer.
            int offset = 0;
            while (offset < buffer.Length)
            {
                int received = await ReceiveAsync(buffer[offset..], cancel).ConfigureAwait(false);
                if (received == 0)
                {
                    throw new InvalidDataException("unexpected end of stream");
                }
                offset += received;
            }
        }

        internal IDisposable? StartScope() => _connection.Logger.StartStreamScope(Id);

        private bool TrySetState(State state, bool shutdown)
        {
            if (((State)Interlocked.Or(ref _state, (int)state)).HasFlag(state))
            {
                return false;
            }
            else
            {
                if (shutdown)
                {
                    TryShutdown();
                }
                return true;
            }
        }

        private enum State : int
        {
            ReadCompleted = 1,
            WriteCompleted = 2,
            Shutdown = 4
        }

        // A System.IO.Stream class to wrap SendAsync/ReceiveAsync functionality of the RpcStream. For Quic,
        // this won't be needed since the QuicStream is a System.IO.Stream.
        private class ByteStream : System.IO.Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotImplementedException();

            public override long Position
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }

            private readonly ReadOnlyMemory<byte>[] _buffers;
            private readonly RpcStream _stream;

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
                ReadAsync(new Memory<byte>(buffer, offset, count), cancel).AsTask();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
            {
                try
                {
                    if (_stream.ReadCompleted)
                    {
                        return 0;
                    }
                    return await _stream.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
                }
                catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamError.StreamingCanceledByWriter)
                {
                    throw new System.IO.IOException("streaming canceled by the writer", ex);
                }
                catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamError.StreamingCanceledByReader)
                {
                    throw new System.IO.IOException("streaming canceled by the reader", ex);
                }
                catch (RpcStreamAbortedException ex)
                {
                    throw new System.IO.IOException($"unexpected streaming error {ex.ErrorCode}", ex);
                }
                catch (Exception ex)
                {
                    throw new System.IO.IOException($"unexpected exception", ex);
                }
            }

            public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotImplementedException();

            public override void SetLength(long value) => throw new NotImplementedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
                WriteAsync(new Memory<byte>(buffer, offset, count), cancel).AsTask();

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
            {
                try
                {
                    _buffers[^1] = buffer;
                    await _stream.SendAsync(_buffers, buffer.Length == 0, cancel).ConfigureAwait(false);
                }
                catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamError.StreamingCanceledByWriter)
                {
                    throw new System.IO.IOException("streaming canceled by the writer", ex);
                }
                catch (RpcStreamAbortedException ex) when (ex.ErrorCode == RpcStreamError.StreamingCanceledByReader)
                {
                    throw new System.IO.IOException("streaming canceled by the reader", ex);
                }
                catch (RpcStreamAbortedException ex)
                {
                    throw new System.IO.IOException($"unexpected streaming error {ex.ErrorCode}", ex);
                }
                catch (Exception ex)
                {
                    throw new System.IO.IOException($"unexpected exception", ex);
                }
            }

            internal ByteStream(RpcStream stream)
            {
                _stream = stream;
                if (_stream.TransportHeader.Length > 0)
                {
                    _buffers = new ReadOnlyMemory<byte>[2];
                    _buffers[0] = _stream.TransportHeader.ToArray();
                }
                else
                {
                    _buffers = new ReadOnlyMemory<byte>[1];
                }
            }
        }
    }
}
