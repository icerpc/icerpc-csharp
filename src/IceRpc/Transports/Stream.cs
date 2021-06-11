// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports
{
    /// <summary>Raised if a stream is aborted. This exception is internal.</summary>
    public class StreamAbortedException : Exception
    {
        internal StreamErrorCode ErrorCode { get; }

        internal StreamAbortedException(StreamErrorCode errorCode) => ErrorCode = errorCode;
    }

    /// <summary>Error codes for stream errors.</summary>
    public enum StreamErrorCode : byte
    {
        /// <summary>The stream was aborted because the invocation was canceled.</summary>
        InvocationCanceled = 0,

        /// <summary>The stream was aborted because the dispatch was canceled.</summary>
        DispatchCanceled = 1,

        /// <summary>An error was encountered while streaming data.</summary>
        StreamingError = 2,

        /// <summary>The stream was aborted because the connection was shutdown.</summary>
        ConnectionShutdown,

        /// <summary>The stream was aborted because the connection was shutdown by the peer.</summary>
        ConnectionShutdownByPeer,

        /// <summary>The stream was aborted because the connection was aborted.</summary>
        ConnectionAborted,
    }

    /// <summary>The Stream abstract base class to be overridden by multi-stream transport implementations.
    /// There's an instance of this class for each active stream managed by the multi-stream connection.</summary>
    public abstract class Stream
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
                _connection.AddStream(value, this, IsControl, ref _id);
            }
        }

        /// <summary>Returns <c>true</c> if the stream is an incoming stream, <c>false</c> otherwise.</summary>
        public bool IsIncoming => _id != -1 && _id % 2 == (_connection.IsIncoming ? 0 : 1);

        /// <summary>Returns <c>true</c> if the stream is a bidirectional stream, <c>false</c> otherwise.</summary>
        public bool IsBidirectional { get; }

        /// <summary>Returns <c>true</c> if the stream is a control stream, <c>false</c> otherwise.</summary>
        public bool IsControl { get; }

        /// <summary>Returns <c>true</c> if the end of stream has been reached, <c>false</c> otherwise.</summary>
        protected internal abstract bool ReceivedEndOfStream { get; }

        /// <summary>The transport header sentinel. Transport implementations that need to add an additional header
        /// to transmit data over the stream can provide the header data here. This can improve performance by reducing
        /// the number of allocations as Ice will allocate buffer space for both the transport header and the Ice
        /// protocol header. If a header is returned here, the implementation of the SendAsync method should expect
        /// this header to be set at the start of the first segment.</summary>
        protected virtual ReadOnlyMemory<byte> TransportHeader => default;

        /// <summary>Get the cancellation dispatch source.</summary>
        internal CancellationTokenSource? CancelDispatchSource { get; }

        internal bool IsIce1 => _connection.Protocol == Protocol.Ice1;

        /// <summary>Returns true if the stream ID is assigned</summary>
        internal bool IsStarted => _id != -1;

        // The use count indicates if the stream is being used to process an invocation or dispatch or
        // to process stream parameters. The stream is disposed only once this count drops to 0.
        private protected int _useCount = 1;

        // Depending on the stream implementation, the _id can be assigned on construction or only once SendAsync
        // is called. Once it's assigned, it's immutable. The specialization of the stream is responsible for not
        // accessing this data member concurrently when it's not safe.
        private long _id = -1;
        private readonly MultiStreamConnection _connection;

        /// <summary>Aborts the stream. This is called by the connection when it's shutdown or aborted.</summary>
        internal void Abort(StreamErrorCode errorCode) => AbortRead(errorCode);

        /// <summary>Receives data from the stream into the returned IO stream.</summary>
        /// <return>The IO stream which can be used to read the data received from the stream.</return>
        public System.IO.Stream ReceiveData()
        {
            if (ReceivedEndOfStream)
            {
                throw new InvalidOperationException("end of stream already reached");
            }
            EnableReceiveFlowControl();
            return new IOStream(this);
        }

        /// <summary>Send data from the given IO stream to the stream.</summary>
        /// <param name="ioStream">The IO stream to read the data to send over the stream.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public void SendData(System.IO.Stream ioStream, CancellationToken cancel = default)
        {
            EnableSendFlowControl();
            Task.Run(async () =>
                {
                    // We use the same default buffer size as System.IO.Stream.CopyToAsync()
                    // TODO: Should this depend on the transport packet size? (Slic default packet size is 32KB for
                    // example).
                    int bufferSize = 81920;
                    if (ioStream.CanSeek)
                    {
                        long remaining = ioStream.Length - ioStream.Position;
                        if (remaining > 0)
                        {
                            // Make sure there's enough space for the transport header
                            remaining += TransportHeader.Length;

                            // In the case of a positive overflow, stick to the default size
                            bufferSize = (int)Math.Min(bufferSize, remaining);
                        }
                    }

                    var receiveBuffer = new ArraySegment<byte>(ArrayPool<byte>.Shared.Rent(bufferSize), 0, bufferSize);
                    try
                    {
                        var sendBuffers = new List<Memory<byte>> { receiveBuffer };
                        int received;
                        do
                        {
                            try
                            {
                                TransportHeader.CopyTo(receiveBuffer);
                                received = await ioStream.ReadAsync(receiveBuffer.Slice(TransportHeader.Length),
                                                                    cancel).ConfigureAwait(false);

                                sendBuffers[0] = receiveBuffer.Slice(0, TransportHeader.Length + received);
                                await SendAsync(sendBuffers.ToReadOnlyMemory(),
                                                received == 0,
                                                cancel).ConfigureAwait(false);
                            }
                            catch
                            {
                                // Don't await the sending of the reset since it might block if sending is blocking.
                                Reset(StreamErrorCode.StreamingError);
                                break;
                            }
                        }
                        while (received > 0);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(receiveBuffer.Array!);

                        Release();
                        ioStream.Dispose();
                    }
                },
                cancel);
        }

        /// <inheritdoc/>
        public override string ToString() => $"{base.ToString()} (ID={Id})";

        /// <summary>Constructs a stream with the given ID.</summary>
        /// <param name="streamId">The stream ID.</param>
        /// <param name="connection">The parent connection.</param>
        protected Stream(MultiStreamConnection connection, long streamId)
        {
            _connection = connection;
            IsBidirectional = streamId % 4 < 2;
            IsControl = streamId == 2 || streamId == 3;
            _connection.AddStream(streamId, this, IsControl, ref _id);
            if (IsIncoming)
            {
                CancelDispatchSource = new CancellationTokenSource();
            }
        }

        /// <summary>Constructs an outgoing stream.</summary>
        /// <param name="bidirectional">True to create a bidirectional stream, False otherwise.</param>
        /// <param name="control">True to create a control stream, False otherwise.</param>
        /// <param name="connection">The parent connection.</param>
        protected Stream(MultiStreamConnection connection, bool bidirectional, bool control)
        {
            _connection = connection;
            IsBidirectional = bidirectional;
            IsControl = control;
        }

        /// <summary>Abort the stream received side.</summary>
        protected abstract void AbortRead(StreamErrorCode errorCode);

        /// <summary>Abort the stream send size.</summary>
        protected abstract void AbortWrite(StreamErrorCode errorCode);

        /// <summary>Enable flow control for receiving data from the peer over the stream. This is called after
        /// receiving a request or response frame to receive data for a stream parameter. Flow control isn't
        /// enabled for receiving the request or response frame whose size is limited with IncomingFrameSizeMax.
        /// The stream relies on the underlying transport flow control instead (TCP, Quic, ...). For stream
        /// parameters, whose size is not limited, it's important that the transport doesn't send an unlimited
        /// amount of data if the receiver doesn't process it. For TCP based transports, this would cause the
        /// send buffer to fill up and this would prevent other streams to be processed.</summary>
        protected virtual void EnableReceiveFlowControl()
        {
            // Increment the use count of this stream to ensure it's not going to be disposed until receiving
            // all the data from the stream.
            Debug.Assert(Thread.VolatileRead(ref _useCount) > 0);
            Interlocked.Increment(ref _useCount);
        }

        /// <summary>Enable flow control for sending data to the peer over the stream. This is called after
        /// sending a request or response frame to send data from a stream parameter.</summary>
        protected virtual void EnableSendFlowControl()
        {
            // Increment the use count of this stream to ensure it's not going to be disposed before sending
            // all the data with the stream.
            Debug.Assert(Thread.VolatileRead(ref _useCount) > 0);
            Interlocked.Increment(ref _useCount);
        }

        /// <summary>Receives data in the given buffer and return the number of received bytes.</summary>
        /// <param name="buffer">The buffer to store the received data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The number of bytes received.</return>
        protected abstract ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Sends data from the given buffers and returns once the buffers are sent.</summary>
        /// <param name="buffers">The buffers with the data to send.</param>
        /// <param name="endStream">True if no more data will be sent over this stream, False otherwise.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        protected abstract ValueTask SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel);

        /// <summary>Releases the stream. This is called when the stream is no longer used either for a request,
        /// response or a streamable parameter. It un-registers the stream from the connection.</summary>
        protected virtual void Shutdown()
        {
            if (IsStarted && !_connection.RemoveStream(Id))
            {
                Debug.Assert(false);
                throw new ObjectDisposedException($"{typeof(Stream).FullName}");
            }
            CancelDispatchSource?.Dispose();
        }

        // Internal method which should only be used by tests.
        internal ValueTask<int> InternalReceiveAsync(Memory<byte> buffer, CancellationToken cancel) =>
            ReceiveAsync(buffer, cancel);

        // Internal method which should only be used by tests.
        internal ValueTask InternalSendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            var array = new ReadOnlyMemory<byte>[buffers.Length + 1];
            array[0] = TransportHeader.ToArray();
            buffers.CopyTo(array.AsMemory()[1..]);
            return SendAsync(array, endStream, cancel);
        }

        internal async ValueTask<((long, long), string)> ReceiveGoAwayFrameAsync()
        {
            Debug.Assert(IsStarted);

            using IDisposable? scope = StartScope();

            byte frameType = IsIce1 ? (byte)Ice1FrameType.CloseConnection : (byte)Ice2FrameType.GoAway;

            ReadOnlyMemory<byte> data =
                await ReceiveFrameAsync(frameType, CancellationToken.None).ConfigureAwait(false);

            long lastBidirectionalId;
            long lastUnidirectionalId;
            string message;
            if (IsIce1)
            {
                // LastResponseStreamId contains the stream ID of the last received response. We make sure to return
                // this stream ID to ensure the request with this stream ID will complete successfully in case the
                // close connection message is received shortly after the response and potentially processed before
                // due to the thread scheduling.
                lastBidirectionalId = _connection.LastResponseStreamId;
                lastUnidirectionalId = 0;
                message = "connection closed gracefull by peer";
            }
            else
            {
                var goAwayFrame = new Ice2GoAwayBody(new InputStream(data, Ice2Definitions.Encoding));
                lastBidirectionalId = goAwayFrame.LastBidirectionalStreamId;
                lastUnidirectionalId = goAwayFrame.LastUnidirectionalStreamId;
                message = goAwayFrame.Message;
            }

            _connection.Logger.LogReceivedGoAwayFrame(_connection, lastBidirectionalId, lastUnidirectionalId, message);
            return ((lastBidirectionalId, lastUnidirectionalId), message);
        }

        internal async ValueTask ReceiveGoAwayCanceledFrameAsync()
        {
            Debug.Assert(IsStarted && !IsIce1);

            using IDisposable? scope = StartScope();

            byte frameType = (byte)Ice2FrameType.GoAwayCanceled;
            ReadOnlyMemory<byte> data =
                await ReceiveFrameAsync(frameType, CancellationToken.None).ConfigureAwait(false);

            _connection.Logger.LogReceivedGoAwayCanceledFrame();
        }

        internal virtual async ValueTask ReceiveInitializeFrameAsync(CancellationToken cancel = default)
        {
            Debug.Assert(IsStarted);
            using IDisposable? scope = StartScope();

            byte frameType = IsIce1 ? (byte)Ice1FrameType.ValidateConnection : (byte)Ice2FrameType.Initialize;

            ReadOnlyMemory<byte> data =
                await ReceiveFrameAsync(frameType, cancel).ConfigureAwait(false);
            if (ReceivedEndOfStream)
            {
                throw new InvalidDataException($"received unexpected end of stream after initialize frame");
            }

            if (IsIce1)
            {
                if (data.Length > 0)
                {
                    throw new InvalidDataException(
                        @$"received an ice1 frame with validate connection type and a size of '{data.Length}' bytes");
                }
            }
            else
            {
                // Read the protocol parameters which are encoded as IceRpc.Fields.
                var istr = new InputStream(data, Ice2Definitions.Encoding);
                int dictionarySize = istr.ReadSize();
                for (int i = 0; i < dictionarySize; ++i)
                {
                    (int key, ReadOnlyMemory<byte> value) = istr.ReadField();
                    if (key == (int)Ice2ParameterKey.IncomingFrameMaxSize)
                    {
                        checked
                        {
                            _connection.PeerIncomingFrameMaxSize = (int)value.Span.ReadVarULong().Value;
                        }

                        if (_connection.PeerIncomingFrameMaxSize < 1024)
                        {
                            throw new InvalidDataException($@"the peer's IncomingFrameMaxSize ({
                                _connection.PeerIncomingFrameMaxSize} bytes) value is inferior to 1KB");
                        }
                    }
                    else
                    {
                        // Ignore unsupported parameters.
                    }
                }

                if (_connection.PeerIncomingFrameMaxSize == null)
                {
                    throw new InvalidDataException("missing IncomingFrameMaxSize Ice2 connection parameter");
                }
            }

            _connection.Logger.LogReceivedInitializeFrame(_connection);
        }

        internal async virtual ValueTask<IncomingRequest> ReceiveRequestFrameAsync(CancellationToken cancel = default)
        {
            byte frameType = IsIce1 ? (byte)Ice1FrameType.Request : (byte)Ice2FrameType.Request;
            ReadOnlyMemory<byte> data = await ReceiveFrameAsync(frameType, cancel).ConfigureAwait(false);
            return new IncomingRequest(_connection.Protocol, data);
        }

        internal async virtual ValueTask<IncomingResponse> ReceiveResponseFrameAsync(
            CancellationToken cancel = default)
        {
            byte frameType = IsIce1 ? (byte)Ice1FrameType.Reply : (byte)Ice2FrameType.Response;
            ReadOnlyMemory<byte> data = await ReceiveFrameAsync(frameType, cancel).ConfigureAwait(false);
            return new IncomingResponse(_connection.Protocol, data);
        }

        internal void Release()
        {
            if (Interlocked.Decrement(ref _useCount) == 0)
            {
                Shutdown();
            }
        }

        internal void Reset(StreamErrorCode errorCode)
        {
            if (!IsControl)
            {
                AbortWrite(errorCode);
            }
        }

        internal virtual async ValueTask SendGoAwayFrameAsync(
            (long Bidirectional, long Unidirectional) streamIds,
            string reason,
            CancellationToken cancel = default)
        {
            Debug.Assert(IsStarted);
            using IDisposable? scope = StartScope();

            if (IsIce1)
            {
                await SendAsync(Ice1Definitions.CloseConnectionFrame, false, cancel).ConfigureAwait(false);
            }
            else
            {
                var data = new List<Memory<byte>>() { new byte[1024] };
                var ostr = new OutputStream(Ice2Definitions.Encoding, data);
                if (!TransportHeader.IsEmpty)
                {
                    ostr.WriteByteSpan(TransportHeader.Span);
                }
                ostr.WriteByte((byte)Ice2FrameType.GoAway);
                OutputStream.Position sizePos = ostr.StartFixedLengthSize();

                var goAwayFrameBody = new Ice2GoAwayBody(streamIds.Bidirectional, streamIds.Unidirectional, reason);
                goAwayFrameBody.IceWrite(ostr);
                ostr.EndFixedLengthSize(sizePos);
                ostr.Finish();

                await SendAsync(data.ToReadOnlyMemory(), false, cancel).ConfigureAwait(false);
            }

            _connection.Logger.LogSentGoAwayFrame(_connection, streamIds.Bidirectional, streamIds.Unidirectional, reason);
        }

        internal virtual async ValueTask SendGoAwayCanceledFrameAsync()
        {
            Debug.Assert(IsStarted && !IsIce1);
            using IDisposable? scope = StartScope();

            var data = new List<Memory<byte>>() { new byte[1024] };
            var ostr = new OutputStream(Ice2Definitions.Encoding, data);
            if (!TransportHeader.IsEmpty)
            {
                ostr.WriteByteSpan(TransportHeader.Span);
            }
            ostr.WriteByte((byte)Ice2FrameType.GoAwayCanceled);
            ostr.EndFixedLengthSize(ostr.StartFixedLengthSize());
            ostr.Finish();

            await SendAsync(data.ToReadOnlyMemory(), true, CancellationToken.None).ConfigureAwait(false);

            _connection.Logger.LogSentGoAwayCanceledFrame();
        }

        internal virtual async ValueTask SendInitializeFrameAsync(CancellationToken cancel = default)
        {
            if (IsIce1)
            {
                await SendAsync(Ice1Definitions.ValidateConnectionFrame, false, cancel).ConfigureAwait(false);
            }
            else
            {
                var data = new List<Memory<byte>>() { new byte[1024] };
                var ostr = new OutputStream(Ice2Definitions.Encoding, data);
                if (!TransportHeader.IsEmpty)
                {
                    ostr.WriteByteSpan(TransportHeader.Span);
                }
                ostr.WriteByte((byte)Ice2FrameType.Initialize);
                OutputStream.Position sizePos = ostr.StartFixedLengthSize();
                OutputStream.Position pos = ostr.Tail;

                // Encode the transport parameters as Fields
                ostr.WriteSize(1);

                // Transmit out local incoming frame maximum size
                Debug.Assert(_connection.IncomingFrameMaxSize > 0);
                ostr.WriteField((int)Ice2ParameterKey.IncomingFrameMaxSize,
                                (ulong)_connection.IncomingFrameMaxSize,
                                OutputStream.IceWriterFromVarULong);

                ostr.EndFixedLengthSize(sizePos);
                ostr.Finish();

                await SendAsync(data.ToReadOnlyMemory(), false, cancel).ConfigureAwait(false);
            }

            using IDisposable? scope = StartScope();
            _connection.Logger.LogSentInitializeFrame(_connection, _connection.IncomingFrameMaxSize);
        }

        internal async ValueTask SendRequestFrameAsync(OutgoingRequest request, CancellationToken cancel = default)
        {
            // Send the request frame.
            await SendFrameAsync(request, cancel).ConfigureAwait(false);

            // If there's a stream data writer, we can start streaming the data.
            request.StreamDataWriter?.Invoke(this);
        }

        internal async ValueTask SendResponseFrameAsync(OutgoingResponse response, CancellationToken cancel = default)
        {
            // Send the response frame.
            await SendFrameAsync(response, cancel).ConfigureAwait(false);

            // If there's a stream data writer, we can start streaming the data.
            response.StreamDataWriter?.Invoke(this);
        }

        internal IDisposable? StartScope() => _connection.Logger.StartStreamScope(Id);

        private protected virtual async ValueTask<ReadOnlyMemory<byte>> ReceiveFrameAsync(
            byte expectedFrameType,
            CancellationToken cancel = default)
        {
            // The default implementation doesn't support Ice1
            Debug.Assert(!IsIce1);

            // Read the Ice2 protocol header (byte frameType, varulong size)
            ArraySegment<byte> buffer = new byte[256];
            await ReceiveFullAsync(buffer.Slice(0, 2), cancel).ConfigureAwait(false);
            var frameType = (Ice2FrameType)buffer[0];
            if ((byte)frameType != expectedFrameType)
            {
                throw new InvalidDataException($"received frame type {frameType} but expected {expectedFrameType}");
            }

            // Read the remainder of the size if needed.
            int sizeLength = buffer[1].ReadSizeLength20();
            if (sizeLength > 1)
            {
                await ReceiveFullAsync(buffer.Slice(2, sizeLength - 1), cancel).ConfigureAwait(false);
            }
            int size = buffer.Slice(1).AsReadOnlySpan().ReadSize20().Size;

            // Read the frame data
            if (size > 0)
            {
                if (size > _connection.IncomingFrameMaxSize)
                {
                    throw new InvalidDataException(
                        $"frame with {size} bytes exceeds IncomingFrameMaxSize connection option value");
                }
                buffer = size > buffer.Count ? new ArraySegment<byte>(new byte[size]) : buffer.Slice(0, size);
                await ReceiveFullAsync(buffer, cancel).ConfigureAwait(false);
            }

            return buffer;
        }

        private protected virtual async ValueTask SendFrameAsync(
            OutgoingFrame frame,
            CancellationToken cancel = default)
        {
            // The default implementation doesn't support Ice1
            Debug.Assert(!IsIce1);

            var headerBuffer = new List<Memory<byte>>(1);
            var ostr = new OutputStream(Encoding.V20, headerBuffer);
            ostr.WriteByteSpan(TransportHeader.Span);

            ostr.Write(frame is OutgoingRequest ? Ice2FrameType.Request : Ice2FrameType.Response);
            OutputStream.Position start = ostr.StartFixedLengthSize(4);
            frame.WriteHeader(ostr);
            ostr.Finish();

            var buffer = new ReadOnlyMemory<byte>[1 + frame.Payload.Length];
            buffer[0] = headerBuffer[0];
            frame.Payload.CopyTo(buffer.AsMemory(1));
            int frameSize = buffer.AsReadOnlyMemory().GetByteCount() - TransportHeader.Length - 1 - 4;
            ostr.RewriteFixedLengthSize20(frameSize, start, 4);

            if (frameSize > _connection.PeerIncomingFrameMaxSize)
            {
                if (frame is OutgoingRequest)
                {
                    throw new ArgumentException(
                        $@"the request size ({frameSize} bytes) is larger than the peer's IncomingFrameSizeMax ({
                        _connection.PeerIncomingFrameMaxSize} bytes)",
                        nameof(frame));
                }
                else
                {
                    // Throw a remote exception instead of this response, the Ice connection will catch it and send it
                    // as the response instead of sending this response which is too large.
                    throw new DispatchException(
                        $@"the response size ({frameSize} bytes) is larger than IncomingFrameSizeMax ({
                        _connection.PeerIncomingFrameMaxSize} bytes)");
                }
            }

            await SendAsync(buffer,
                            endStream: frame.StreamDataWriter == null,
                            cancel).ConfigureAwait(false);
        }

        private async ValueTask ReceiveFullAsync(Memory<byte> buffer, CancellationToken cancel = default)
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

        private class IOStream : System.IO.Stream
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

            private readonly Stream _stream;

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

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel) =>
                _stream.ReceiveAsync(buffer, cancel);

            public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotImplementedException();
            public override void SetLength(long value) => throw new NotImplementedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    _stream.Release();
                }
            }

            internal IOStream(Stream stream) => _stream = stream;
        }
    }
}
