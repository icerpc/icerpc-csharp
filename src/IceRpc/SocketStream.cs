// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The SocketStream abstract base class to be overridden by multi-stream transport implementations.
    /// There's an instance of this class for each active stream managed by the multi-stream socket.</summary>
    public abstract class SocketStream
    {
        /// <summary>A delegate used to send data from a System.IO.Stream value.</summary>
        public static readonly Action<SocketStream, System.IO.Stream, CancellationToken> IceSendDataFromIOStream =
            (socketStream, value, cancel) => socketStream.SendDataFromIOStream(value, cancel);

        /// <summary>A delegate used to receive data into a System.IO.Stream value.</summary>
        public static readonly Func<SocketStream, System.IO.Stream> IceReceiveDataIntoIOStream =
            socketStream => socketStream.ReceiveDataIntoIOStream();

        /// <summary>The stream ID. If the stream ID hasn't been assigned yet, an exception is thrown. Assigning the
        /// stream ID registers the stream with the socket.</summary>
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
                // First add the stream and then assign the ID. AddStream can throw if the socket is closed and
                // in this case we want to make sure the id isn't assigned since the stream isn't considered
                // allocated if not added to the socket.
                _socket.AddStream(value, this, IsControl, ref _id);
            }
        }

        /// <summary>Returns True if the stream is an incoming stream, False otherwise.</summary>
        public bool IsIncoming => _id != -1 && _id % 2 == (_socket.IsIncoming ? 0 : 1);

        /// <summary>Returns True if the stream is a bidirectional stream, False otherwise.</summary>
        public bool IsBidirectional { get; }

        /// <summary>Returns True if the stream is a control stream, False otherwise.</summary>
        public bool IsControl { get; }

        protected internal abstract bool ReceivedEndOfStream { get; }

        /// <summary>The transport header sentinel. Transport implementations that need to add an additional header
        /// to transmit data over the stream can provide the header data here. This can improve performance by reducing
        /// the number of allocations as Ice will allocate buffer space for both the transport header and the Ice
        /// protocol header. If a header is returned here, the implementation of the SendAsync method should expect
        /// this header to be set at the start of the first segment.</summary>
        protected virtual ReadOnlyMemory<byte> TransportHeader => default;

        /// <summary>The Reset event is triggered when a reset frame is received.</summary>
        internal event Action<long>? Reset;

        /// <summary>Returns true if the stream ID is assigned</summary>
        internal bool IsStarted => _id != -1;

        // The use count indicates if the socket stream is being used to process an invocation or dispatch or
        // to process stream parameters. The socket stream is disposed only once this count drops to 0.
        private protected int _useCount = 1;

        // Depending on the stream implementation, the _id can be assigned on construction or only once SendAsync
        // is called. Once it's assigned, it's immutable. The specialization of the stream is responsible for not
        // accessing this data member concurrently when it's not safe.
        private long _id = -1;
        private readonly MultiStreamSocket _socket;

        /// <summary>Aborts the stream. This is called by the connection when it's being closed. If needed, the stream
        /// implementation should abort the pending receive task.</summary>
        public abstract void Abort(Exception ex);

        /// <summary>Receives data from the socket stream into the returned IO stream.</summary>
        /// <return>The IO stream which can be used to read the data received from the stream.</return>
        public System.IO.Stream ReceiveDataIntoIOStream() => new IOStream(this);

        /// <summary>Send data from the given IO stream to the socket stream.</summary>
        /// <param name="ioStream">The IO stream to read the data to send over the socket stream.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public void SendDataFromIOStream(System.IO.Stream ioStream, CancellationToken cancel)
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
                        var sendBuffers = new List<ArraySegment<byte>> { receiveBuffer };
                        int received;
                        do
                        {
                            try
                            {
                                TransportHeader.CopyTo(receiveBuffer);
                                received = await ioStream.ReadAsync(receiveBuffer.Slice(TransportHeader.Length),
                                                                    cancel).ConfigureAwait(false);

                                sendBuffers[0] = receiveBuffer.Slice(0, TransportHeader.Length + received);
                                await SendAsync(sendBuffers, received == 0, cancel).ConfigureAwait(false);
                            }
                            catch
                            {
                                // Don't await the sending of the reset since it might block if sending is blocking.
                                _ = ResetAsync((long)StreamResetErrorCode.StopStreamingData).AsTask();
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

        public override string ToString() => $"{base.ToString()} (ID={Id})";

        /// <summary>Receives data in the given buffer and return the number of received bytes.</summary>
        /// <param name="buffer">The buffer to store the received data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The number of bytes received.</return>
        protected abstract ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Resets the stream.</summary>
        /// <param name="errorCode">The error code indicating the reason of the reset to transmit to the peer.</param>
        protected abstract ValueTask ResetAsync(long errorCode);

        /// <summary>Sends data from the given buffer and returns once the buffer is sent.</summary>
        /// <param name="buffer">The buffer with the data to send.</param>
        /// <param name="fin">True if no more data will be sent over this stream, False otherwise.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        protected abstract ValueTask SendAsync(
            IList<ArraySegment<byte>> buffer,
            bool fin,
            CancellationToken cancel);

        /// <summary>Constructs a stream with the given ID.</summary>
        /// <param name="streamId">The stream ID.</param>
        /// <param name="socket">The parent socket.</param>
        protected SocketStream(MultiStreamSocket socket, long streamId)
        {
            _socket = socket;
            IsBidirectional = streamId % 4 < 2;
            IsControl = streamId == 2 || streamId == 3;
            _socket.AddStream(streamId, this, IsControl, ref _id);
        }

        /// <summary>Constructs an outgoing stream.</summary>
        /// <param name="bidirectional">True to create a bidirectional stream, False otherwise.</param>
        /// <param name="control">True to create a control stream, False otherwise.</param>
        /// <param name="socket">The parent socket.</param>
        protected SocketStream(MultiStreamSocket socket, bool bidirectional, bool control)
        {
            _socket = socket;
            IsBidirectional = bidirectional;
            IsControl = control;
        }

        /// <summary>Releases the stream. This is called when the stream is no longer used either for a request,
        /// response or a streamable parameter. It un-registers the stream from the socket.</summary>
        protected virtual void Shutdown()
        {
            if (IsStarted && !_socket.RemoveStream(Id))
            {
                Debug.Assert(false);
                throw new ObjectDisposedException($"{typeof(SocketStream).FullName}");
            }
        }

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

        internal virtual async ValueTask<((long, long), string)> ReceiveGoAwayFrameAsync()
        {
            Debug.Assert(IsStarted);
            using IDisposable? scope = StartScope();

            byte frameType = _socket.Protocol == Protocol.Ice1 ?
                (byte)Ice1FrameType.CloseConnection : (byte)Ice2FrameType.GoAway;

            ArraySegment<byte> data = await ReceiveFrameAsync(frameType, CancellationToken.None).ConfigureAwait(false);
            if (!ReceivedEndOfStream)
            {
                throw new InvalidDataException($"expected end of stream after GoAway frame");
            }

            long lastBidirectionalId;
            long lastUnidirectionalId;
            string message;
            if (_socket.Protocol == Protocol.Ice1)
            {
                // LastResponseStreamId contains the stream ID of the last received response. We make sure to return
                // this stream ID to ensure the request with this stream ID will complete successfully in case the
                // close connection message is received shortly after the response and potentially processed before
                // due to the thread scheduling.
                lastBidirectionalId = _socket.LastResponseStreamId;
                lastUnidirectionalId = 0;
                message = "connection gracefully closed by peer";
            }
            else
            {
                var goAwayFrame = new Ice2GoAwayBody(new InputStream(data, Ice2Definitions.Encoding));
                lastBidirectionalId = (long)goAwayFrame.LastBidirectionalStreamId;
                lastUnidirectionalId = (long)goAwayFrame.LastUnidirectionalStreamId;
                message = goAwayFrame.Message;
            }

            _socket.Logger.LogReceivedGoAwayFrame(_socket, lastBidirectionalId, lastUnidirectionalId, message);
            return ((lastBidirectionalId, lastUnidirectionalId), message);
        }

        internal virtual void ReceivedReset(long errorCode) => Reset?.Invoke(errorCode);

        internal virtual async ValueTask ReceiveInitializeFrameAsync(CancellationToken cancel = default)
        {
            Debug.Assert(IsStarted);
            using IDisposable? scope = StartScope();

            byte frameType = _socket.Protocol == Protocol.Ice1 ?
                (byte)Ice1FrameType.ValidateConnection : (byte)Ice2FrameType.Initialize;

            ArraySegment<byte> data = await ReceiveFrameAsync(frameType, cancel).ConfigureAwait(false);
            if (ReceivedEndOfStream)
            {
                throw new InvalidDataException($"received unexpected end of stream after initialize frame");
            }

            if (_socket.Protocol == Protocol.Ice1)
            {
                if (data.Count > 0)
                {
                    throw new InvalidDataException(
                        @$"received an ice1 frame with validate connection type and a size of '{data.Count}' bytes");
                }
            }
            else
            {
                // Read the protocol parameters which are encoded with the binary context encoding.
                var istr = new InputStream(data, Ice2Definitions.Encoding);
                int dictionarySize = istr.ReadSize();
                for (int i = 0; i < dictionarySize; ++i)
                {
                    (int key, ReadOnlyMemory<byte> value) = istr.ReadBinaryContextEntry();
                    if (key == (int)Ice2ParameterKey.IncomingFrameMaxSize)
                    {
                        checked
                        {
                            _socket.PeerIncomingFrameMaxSize = (int)value.Span.ReadVarULong().Value;
                        }

                        if (_socket.PeerIncomingFrameMaxSize < 1024)
                        {
                            throw new InvalidDataException($@"the peer's IncomingFrameMaxSize ({
                                _socket.PeerIncomingFrameMaxSize} bytes) value is inferior to 1KB");
                        }
                    }
                    else
                    {
                        // Ignore unsupported parameters.
                    }
                }

                if (_socket.PeerIncomingFrameMaxSize == null)
                {
                    throw new InvalidDataException("missing IncomingFrameMaxSize Ice2 connection parameter");
                }
            }

            _socket.Logger.LogReceivedInitializeFrame(_socket);
        }

        internal async virtual ValueTask<IncomingRequest> ReceiveRequestFrameAsync(CancellationToken cancel = default)
        {
            byte frameType = _socket.Protocol == Protocol.Ice1 ?
                (byte)Ice1FrameType.Request : (byte)Ice2FrameType.Request;

            ArraySegment<byte> data = await ReceiveFrameAsync(frameType, cancel).ConfigureAwait(false);

            IncomingRequest request;
            if (ReceivedEndOfStream)
            {
                request = new IncomingRequest(_socket.Protocol, data, null);
            }
            else
            {
                EnableReceiveFlowControl();
                request = new IncomingRequest(_socket.Protocol, data, this);
            }

            return request;
        }

        internal async virtual ValueTask<IncomingResponse> ReceiveResponseFrameAsync(
            CancellationToken cancel = default)
        {
            ArraySegment<byte> data;
            try
            {
                byte frameType = _socket.Protocol == Protocol.Ice1 ?
                    (byte)Ice1FrameType.Reply : (byte)Ice2FrameType.Response;

                data = await ReceiveFrameAsync(frameType, cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (_socket.Protocol != Protocol.Ice1)
                {
                    // Don't await the sending of the reset since it might block if sending is blocking.
                    _ = ResetAsync((long)StreamResetErrorCode.RequestCanceled).AsTask();
                }
                throw;
            }

            IncomingResponse response;
            if (ReceivedEndOfStream)
            {
                response = new IncomingResponse(_socket.Protocol, data, null);
            }
            else
            {
                EnableReceiveFlowControl();
                response = new IncomingResponse(_socket.Protocol, data, this);
            }

            return response;
        }

        internal virtual async ValueTask SendGoAwayFrameAsync(
            (long Bidirectional, long Unidirectional) streamIds,
            string reason,
            CancellationToken cancel = default)
        {
            Debug.Assert(IsStarted);
            using IDisposable? scope = StartScope();

            if (_socket.Protocol == Protocol.Ice1)
            {
                await SendAsync(Ice1Definitions.CloseConnectionFrame, true, cancel).ConfigureAwait(false);
            }
            else
            {
                var data = new List<ArraySegment<byte>>() { new byte[1024] };
                var ostr = new OutputStream(Ice2Definitions.Encoding, data);
                if (!TransportHeader.IsEmpty)
                {
                    ostr.WriteByteSpan(TransportHeader.Span);
                }
                ostr.WriteByte((byte)Ice2FrameType.GoAway);
                OutputStream.Position sizePos = ostr.StartFixedLengthSize();

                var goAwayFrameBody = new Ice2GoAwayBody(
                    (ulong)streamIds.Bidirectional,
                    (ulong)streamIds.Unidirectional,
                    reason);
                goAwayFrameBody.IceWrite(ostr);
                ostr.EndFixedLengthSize(sizePos);
                ostr.Finish();

                await SendAsync(data, true, cancel).ConfigureAwait(false);
            }

            _socket.Logger.LogSentGoAwayFrame(_socket, streamIds.Bidirectional, streamIds.Unidirectional, reason);
        }

        internal virtual async ValueTask SendInitializeFrameAsync(CancellationToken cancel = default)
        {
            if (_socket.Protocol == Protocol.Ice1)
            {
                await SendAsync(Ice1Definitions.ValidateConnectionFrame, false, cancel).ConfigureAwait(false);
            }
            else
            {
                var data = new List<ArraySegment<byte>>() { new byte[1024] };
                var ostr = new OutputStream(Ice2Definitions.Encoding, data);
                if (!TransportHeader.IsEmpty)
                {
                    ostr.WriteByteSpan(TransportHeader.Span);
                }
                ostr.WriteByte((byte)Ice2FrameType.Initialize);
                OutputStream.Position sizePos = ostr.StartFixedLengthSize();
                OutputStream.Position pos = ostr.Tail;

                // Encode the transport parameters with the binary context encoding.
                ostr.WriteSize(1);

                // Transmit out local incoming frame maximum size
                Debug.Assert(_socket.IncomingFrameMaxSize > 0);
                ostr.WriteBinaryContextEntry((int)Ice2ParameterKey.IncomingFrameMaxSize,
                                             (ulong)_socket.IncomingFrameMaxSize,
                                             OutputStream.IceWriterFromVarULong);

                ostr.EndFixedLengthSize(sizePos);
                ostr.Finish();

                await SendAsync(data, false, cancel).ConfigureAwait(false);
            }

            using IDisposable? scope = StartScope();
            _socket.Logger.LogSentInitializeFrame(_socket, _socket.IncomingFrameMaxSize);
        }

        internal async ValueTask SendRequestFrameAsync(OutgoingRequest request, CancellationToken cancel = default)
        {
            try
            {
                // Send the request frame.
                await SendFrameAsync(request, cancel).ConfigureAwait(false);

                // If there's a stream data writer, we can start streaming the data.
                request.StreamDataWriter?.Invoke(this);
            }
            catch (OperationCanceledException)
            {
                // If the stream is not started, there's no need to send a stream reset frame. The stream ID wasn't
                // allocated and the peer doesn't know about this stream.
                if (IsStarted && _socket.Protocol != Protocol.Ice1)
                {
                    // Don't await the sending of the reset since it might block if sending is blocking.
                    _ = ResetAsync((long)StreamResetErrorCode.RequestCanceled).AsTask();
                }
                throw;
            }
        }

        internal async ValueTask SendResponseFrameAsync(
            OutgoingResponse response,
            CancellationToken cancel = default)
        {
            // Send the response frame.
            await SendFrameAsync(response, cancel).ConfigureAwait(false);

            // If there's a stream data writer, we can start streaming the data.
            response.StreamDataWriter?.Invoke(this);
        }

        internal IDisposable? StartScope() => _socket.Logger.StartStreamScope(Id);

        internal void Release()
        {
            if (Interlocked.Decrement(ref _useCount) == 0)
            {
                Shutdown();
            }
        }

        private protected virtual async ValueTask<ArraySegment<byte>> ReceiveFrameAsync(
            byte expectedFrameType,
            CancellationToken cancel = default)
        {
            // The default implementation only supports the Ice2 protocol
            Debug.Assert(_socket.Protocol == Protocol.Ice2);

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
                if (size > _socket.IncomingFrameMaxSize)
                {
                    throw new InvalidDataException(
                        $"frame with {size} bytes exceeds IncomingFrameMaxSize connection option value");
                }
                buffer = size > buffer.Array!.Length ? new ArraySegment<byte>(new byte[size]) : buffer.Slice(0, size);
                await ReceiveFullAsync(buffer, cancel).ConfigureAwait(false);
            }

            return buffer;
        }

        private protected virtual async ValueTask SendFrameAsync(
            OutgoingFrame frame,
            CancellationToken cancel = default)
        {
            // The default implementation only supports the Ice2 protocol
            Debug.Assert(_socket.Protocol == Protocol.Ice2);

            var buffer = new List<ArraySegment<byte>>(frame.Payload.Count + 1);
            var ostr = new OutputStream(Encoding.V20, buffer);
            ostr.WriteByteSpan(TransportHeader.Span);

            ostr.Write(frame is OutgoingRequest ? Ice2FrameType.Request : Ice2FrameType.Response);
            OutputStream.Position start = ostr.StartFixedLengthSize(4);
            frame.WriteHeader(ostr);
            ostr.Finish();

            buffer.AddRange(frame.Payload);
            int frameSize = buffer.GetByteCount() - TransportHeader.Length - 1 - 4;
            ostr.RewriteFixedLengthSize20(frameSize, start, 4);

            if (frameSize > _socket.PeerIncomingFrameMaxSize)
            {
                if (frame is OutgoingRequest)
                {
                    throw new LimitExceededException(
                        $@"the request size ({frameSize} bytes) is larger than the peer's IncomingFrameSizeMax ({
                        _socket.PeerIncomingFrameMaxSize} bytes)");
                }
                else
                {
                    // Throw a remote exception instead of this response, the Ice connection will catch it and send it
                    // as the response instead of sending this response which is too large.
                    throw new ServerException(
                        $@"the response size ({frameSize} bytes) is larger than IncomingFrameSizeMax ({
                        _socket.PeerIncomingFrameMaxSize} bytes)");
                }
            }

            await SendAsync(buffer, fin: frame.StreamDataWriter == null, cancel).ConfigureAwait(false);
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

            private readonly SocketStream _stream;

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

            internal IOStream(SocketStream stream) => _stream = stream;
        }
    }
}
