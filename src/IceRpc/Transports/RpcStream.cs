// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
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

    /// <summary>The RpcStream abstract base class to be overridden by multi-stream transport implementations.
    /// There's an instance of this class for each active stream managed by the multi-stream connection.</summary>
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
                _connection.AddStream(value, this, IsControl, ref _id);
            }
        }

        /// <summary>Returns <c>true</c> if the stream is an incoming stream, <c>false</c> otherwise.</summary>
        public bool IsIncoming => _id != -1 && _id % 2 == (_connection.IsServer ? 0 : 1);

        /// <summary>Returns <c>true</c> if the stream is a bidirectional stream, <c>false</c> otherwise.</summary>
        public bool IsBidirectional { get; }

        /// <summary>Returns <c>true</c> if the stream is a control stream, <c>false</c> otherwise.</summary>
        public bool IsControl { get; }

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

        internal bool IsIce1 => _connection.Protocol == Protocol.Ice1;

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
            IsControl = streamId == 2 || streamId == 3;
            _connection.AddStream(streamId, this, IsControl, ref _id);
            if (IsIncoming)
            {
                CancelDispatchSource = new CancellationTokenSource();
                if (!IsBidirectional)
                {
                    // Write-side of incoming unidirectional stream is marked as completed since there should be
                    // no writes on the stream.
                    TrySetWriteCompleted();
                }
            }
            else if (!IsBidirectional)
            {
                // Read-side of outgoing unidirectional stream is marked as completed since there should be
                // no reads on the stream.
                TrySetReadCompleted();
            }
        }

        /// <summary>Constructs an outgoing stream.</summary>
        /// <param name="bidirectional"><c>true</c> to create a bidirectional stream, <c>false</c> otherwise.</param>
        /// <param name="control"><c>true</c> to create a control stream, <c>false</c> otherwise.</param>
        /// <param name="connection">The parent connection.</param>
        protected RpcStream(MultiStreamConnection connection, bool bidirectional, bool control)
        {
            _connection = connection;
            IsBidirectional = bidirectional;
            IsControl = control;
            if (!IsBidirectional)
            {
                // Read-side of outgoing unidirectional stream is marked as completed since there should be
                // no reads on the stream.
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
                Shutdown();
            }
        }

        internal void Abort(RpcStreamError errorCode)
        {
            // Abort writes.
            AbortWrite(errorCode);

            // Abort reads.
            AbortRead(errorCode);
        }

        internal async ValueTask<((long, long), string)> ReceiveGoAwayFrameAsync()
        {
            Debug.Assert(IsStarted);

            long lastBidirectionalId;
            long lastUnidirectionalId;
            string message;
            if (IsIce1)
            {
                ReadOnlyMemory<byte> buffer = await ReceiveIce1FrameAsync(
                    Ice1FrameType.CloseConnection,
                    CancellationToken.None).ConfigureAwait(false);
                if (buffer.Length > 0)
                {
                    throw new InvalidDataException(
                        @$"received an ice1 frame with close connection type and a size of '{buffer.Length}' bytes");
                }

                // LastResponseStreamId contains the stream ID of the last received response. We make sure to return
                // this stream ID to ensure the request with this stream ID will complete successfully in case the
                // close connection message is received shortly after the response and potentially processed before
                // due to the thread scheduling.
                lastBidirectionalId = _connection.LastResponseStreamId;
                lastUnidirectionalId = 0;
                message = "connection closed gracefully by peer";
            }
            else
            {
                ReadOnlyMemory<byte> buffer = await ReceiveIce2FrameAsync(
                    Ice2FrameType.GoAway,
                    CancellationToken.None).ConfigureAwait(false);

                var goAwayFrame = new Ice2GoAwayBody(new IceDecoder(buffer, Ice2Definitions.Encoding));
                lastBidirectionalId = goAwayFrame.LastBidirectionalStreamId;
                lastUnidirectionalId = goAwayFrame.LastUnidirectionalStreamId;
                message = goAwayFrame.Message;
            }

            _connection.Logger.LogReceivedGoAwayFrame(_connection, lastBidirectionalId, lastUnidirectionalId, message);
            return ((lastBidirectionalId, lastUnidirectionalId), message);
        }

        internal async ValueTask ReceiveGoAwayCanceledFrameAsync(CancellationToken cancel)
        {
            Debug.Assert(IsStarted && !IsIce1);
            ReadOnlyMemory<byte> buffer = await ReceiveIce2FrameAsync(
                Ice2FrameType.GoAwayCanceled,
                cancel).ConfigureAwait(false);
            if (buffer.Length > 0)
            {
                throw new InvalidDataException(
                    @$"received an ice2 frame with go away canceled type and a size of '{buffer.Length}' bytes");
            }
            _connection.Logger.LogReceivedGoAwayCanceledFrame();
        }

        internal virtual async ValueTask ReceiveInitializeFrameAsync(CancellationToken cancel = default)
        {
            Debug.Assert(IsStarted);

            if (IsIce1)
            {
                ReadOnlyMemory<byte> buffer = await ReceiveIce1FrameAsync(
                    Ice1FrameType.ValidateConnection,
                    cancel).ConfigureAwait(false);
                if (buffer.Length > 0)
                {
                    throw new InvalidDataException(
                        @$"received an ice1 frame with validate connection type and a size of '{buffer.Length}' bytes");
                }
            }
            else
            {
                ReadOnlyMemory<byte> buffer = await ReceiveIce2FrameAsync(
                    Ice2FrameType.Initialize,
                    cancel).ConfigureAwait(false);

                // Read the protocol parameters which are encoded as IceRpc.Fields.
                var decoder = new IceDecoder(buffer, Ice2Definitions.Encoding);
                int dictionarySize = decoder.DecodeSize();
                for (int i = 0; i < dictionarySize; ++i)
                {
                    (int key, ReadOnlyMemory<byte> value) = decoder.DecodeField();
                    if (key == (int)Ice2ParameterKey.IncomingFrameMaxSize)
                    {
                        checked
                        {
                            _connection.PeerIncomingFrameMaxSize = (int)value.Span.DecodeVarULong().Value;
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

        internal virtual async ValueTask<IncomingRequest> ReceiveRequestFrameAsync(CancellationToken cancel = default)
        {
            IncomingRequest request;
            int payloadSize;
            if (IsIce1)
            {
                ReadOnlyMemory<byte> buffer = await ReceiveIce1FrameAsync(
                    Ice1FrameType.Request,
                    cancel).ConfigureAwait(false);

                var decoder = new IceDecoder(buffer, _connection.Protocol.GetEncoding());

                var requestHeader = new Ice1RequestHeader(decoder);
                if (requestHeader.IdentityAndFacet.Identity.Name.Length == 0)
                {
                    throw new InvalidDataException("received ice1 request with empty identity name");
                }

                request = new IncomingRequest
                {
                    Protocol = Protocol.Ice1,
                    Path = requestHeader.IdentityAndFacet.ToPath(),
                    Operation = requestHeader.Operation,
                    IsIdempotent = requestHeader.OperationMode != OperationMode.Normal,
                    IsOneway = !IsBidirectional,
                    PayloadEncoding =
                        Encoding.FromMajorMinor(requestHeader.PayloadEncodingMajor, requestHeader.PayloadEncodingMinor),
                    Priority = default,
                    Deadline = DateTime.MaxValue,
                    Payload = buffer[decoder.Pos..]
                };

                if (requestHeader.Context.Count > 0)
                {
                    request.Features = new FeatureCollection();
                    request.Features.Set(new Context { Value = requestHeader.Context });
                }

                // The payload size is the encapsulation size less the 6 bytes of the encapsulation header.
                payloadSize = requestHeader.EncapsulationSize - 6;
            }
            else
            {
                ReadOnlyMemory<byte> buffer = await ReceiveIce2FrameAsync(
                    Ice2FrameType.Request,
                    cancel).ConfigureAwait(false);

                var decoder = new IceDecoder(buffer, _connection.Protocol.GetEncoding());
                int headerSize = decoder.DecodeSize();
                int headerStartPos = decoder.Pos;

                // We use the generated code for the header body and read the rest of the header "by hand".
                var requestHeaderBody = new Ice2RequestHeaderBody(decoder);
                if (requestHeaderBody.Deadline < -1 || requestHeaderBody.Deadline == 0)
                {
                    throw new InvalidDataException($"received invalid deadline value {requestHeaderBody.Deadline}");
                }
                IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields = decoder.DecodeFieldDictionary();
                payloadSize = decoder.DecodeSize();
                if (decoder.Pos - headerStartPos != headerSize)
                {
                    throw new InvalidDataException(
                        @$"received invalid request header: expected {headerSize} bytes but read {
                            decoder.Pos - headerStartPos} bytes");
                }

                request = new IncomingRequest
                {
                    Protocol = _connection.Protocol,
                    Path = requestHeaderBody.Path,
                    Operation  = requestHeaderBody.Operation,
                    IsIdempotent = requestHeaderBody.Idempotent ?? false,
                    IsOneway = !IsBidirectional,
                    Priority = requestHeaderBody.Priority ?? default,
                    // The infinite deadline is encoded as -1 and converted to DateTime.MaxValue
                    Deadline = requestHeaderBody.Deadline == -1 ?
                        DateTime.MaxValue : DateTime.UnixEpoch + TimeSpan.FromMilliseconds(requestHeaderBody.Deadline),
                    PayloadEncoding = requestHeaderBody.PayloadEncoding is string payloadEncoding ?
                        Encoding.FromString(payloadEncoding) : Ice2Definitions.Encoding,
                    Fields = fields,
                    Payload = buffer[decoder.Pos..]
                };

                // Decode Context from Fields and set corresponding feature.
                if (request.Fields.TryGetValue((int)Ice2FieldKey.Context, out ReadOnlyMemory<byte> value))
                {
                    request.Features = new FeatureCollection();
                    request.Features.Set(new Context
                    {
                        Value = value.DecodeFieldValue(decoder => decoder.DecodeDictionary(
                            minKeySize: 1,
                            minValueSize: 1,
                            keyDecodeFunc: decoder => decoder.DecodeString(),
                            valueDecodeFunc: decoder => decoder.DecodeString()))
                    });
                }
            }

            if (payloadSize != request.Payload.Length)
            {
                throw new InvalidDataException(
                    $"request payload size mismatch: expected {payloadSize} bytes, read {request.Payload.Length} bytes");
            }

            if (request.Operation.Length == 0)
            {
                throw new InvalidDataException("received request with empty operation name");
            }

            return request;
        }

        internal virtual async ValueTask<IncomingResponse> ReceiveResponseFrameAsync(
            CancellationToken cancel = default)
        {
            IncomingResponse response;
            int? payloadSize = null;
            if (IsIce1)
            {
                ReadOnlyMemory<byte> buffer = await ReceiveIce1FrameAsync(
                    Ice1FrameType.Reply,
                    cancel).ConfigureAwait(false);

                var decoder = new IceDecoder(buffer, _connection.Protocol.GetEncoding());

                ReplyStatus replyStatus = decoder.DecodeReplyStatus();
                Encoding payloadEncoding;
                if (replyStatus <= ReplyStatus.UserException)
                {
                    var responseHeader = new Ice1ResponseHeader(decoder);
                    payloadEncoding = Encoding.FromMajorMinor(responseHeader.PayloadEncodingMajor,
                                                              responseHeader.PayloadEncodingMinor);
                    payloadSize = responseHeader.EncapsulationSize - 6;
                }
                else
                {
                    // "special" exception
                    payloadEncoding = Encoding.Ice11;
                }

                response = new IncomingResponse
                {
                    Protocol = Protocol.Ice1,
                    ReplyStatus = replyStatus,
                    ResultType = replyStatus == ReplyStatus.OK ? ResultType.Success : ResultType.Failure,
                    PayloadEncoding = payloadEncoding,
                    Payload = buffer[decoder.Pos..]
                };
            }
            else
            {
                ReadOnlyMemory<byte> buffer = await ReceiveIce2FrameAsync(
                    Ice2FrameType.Response,
                    cancel).ConfigureAwait(false);

                var decoder = new IceDecoder(buffer, _connection.Protocol.GetEncoding());
                int headerSize = decoder.DecodeSize();
                int headerStartPos = decoder.Pos;

                var responseHeaderBody = new Ice2ResponseHeaderBody(decoder);
                IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields = decoder.DecodeFieldDictionary();
                payloadSize = decoder.DecodeSize();
                if (decoder.Pos - headerStartPos != headerSize)
                {
                    throw new InvalidDataException(
                        @$"received invalid response header: expected {headerSize} bytes but read {
                            decoder.Pos - headerStartPos} bytes");
                }

                Encoding payloadEncoding = responseHeaderBody.PayloadEncoding is string encoding ?
                        Encoding.FromString(encoding) : Ice2Definitions.Encoding;
                ReplyStatus replyStatus;
                if (responseHeaderBody.ResultType == ResultType.Failure && payloadEncoding == Encoding.Ice11)
                {
                    replyStatus = buffer.Span[decoder.Pos].AsReplyStatus(); // first byte of the payload
                }
                else
                {
                    replyStatus = responseHeaderBody.ResultType == ResultType.Success ?
                        ReplyStatus.OK : ReplyStatus.UserException;
                }

                response = new IncomingResponse
                {
                    Protocol = Protocol.Ice2,
                    ResultType = responseHeaderBody.ResultType,
                    PayloadEncoding = payloadEncoding,
                    Fields = fields,
                    Payload = buffer[decoder.Pos..],
                    ReplyStatus = replyStatus,
                };
            }

            if (payloadSize != null && payloadSize != response.Payload.Length)
            {
                throw new InvalidDataException(
                    @$"response payload size mismatch: expected {payloadSize} bytes, read
                        {response.Payload.Length} bytes");
            }

            return response;
        }

        internal virtual async ValueTask SendGoAwayFrameAsync(
            (long Bidirectional, long Unidirectional) streamIds,
            string reason,
            CancellationToken cancel = default)
        {
            Debug.Assert(IsStarted);

            if (IsIce1)
            {
                await SendAsync(Ice1Definitions.CloseConnectionFrame, true, cancel).ConfigureAwait(false);
            }
            else
            {
                var bufferWriter = new BufferWriter(new byte[1024]);
                var encoder = new Ice20Encoder(bufferWriter);
                if (!TransportHeader.IsEmpty)
                {
                    bufferWriter.WriteByteSpan(TransportHeader.Span);
                }
                encoder.EncodeByte((byte)Ice2FrameType.GoAway);
                BufferWriter.Position sizePos = encoder.StartFixedLengthSize();

                var goAwayFrameBody = new Ice2GoAwayBody(streamIds.Bidirectional, streamIds.Unidirectional, reason);
                goAwayFrameBody.Encode(encoder);
                encoder.EndFixedLengthSize(sizePos);

                await SendAsync(bufferWriter.Finish(), false, cancel).ConfigureAwait(false);
            }

            _connection.Logger.LogSentGoAwayFrame(_connection, streamIds.Bidirectional, streamIds.Unidirectional, reason);
        }

        internal virtual async ValueTask SendGoAwayCanceledFrameAsync()
        {
            Debug.Assert(IsStarted && !IsIce1);

            var bufferWriter = new BufferWriter(new byte[1024]);
            var encoder = new Ice20Encoder(bufferWriter);
            if (!TransportHeader.IsEmpty)
            {
                bufferWriter.WriteByteSpan(TransportHeader.Span);
            }
            encoder.EncodeByte((byte)Ice2FrameType.GoAwayCanceled);
            encoder.EndFixedLengthSize(encoder.StartFixedLengthSize());
            await SendAsync(bufferWriter.Finish(), false, CancellationToken.None).ConfigureAwait(false);

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
                var bufferWriter = new BufferWriter(new byte[1024]);
                var encoder = new Ice20Encoder(bufferWriter);
                if (!TransportHeader.IsEmpty)
                {
                    bufferWriter.WriteByteSpan(TransportHeader.Span);
                }
                encoder.EncodeByte((byte)Ice2FrameType.Initialize);
                BufferWriter.Position sizePos = encoder.StartFixedLengthSize();
                BufferWriter.Position pos = bufferWriter.Tail;

                // Encode the transport parameters as Fields
                encoder.EncodeSize(1);

                // Transmit out local incoming frame maximum size
                Debug.Assert(_connection.IncomingFrameMaxSize > 0);
                encoder.EncodeField((int)Ice2ParameterKey.IncomingFrameMaxSize,
                                (ulong)_connection.IncomingFrameMaxSize,
                                (encoder, value) => encoder.EncodeVarULong(value));

                encoder.EndFixedLengthSize(sizePos);

                await SendAsync(bufferWriter.Finish(), false, cancel).ConfigureAwait(false);
            }

            _connection.Logger.LogSentInitializeFrame(_connection, _connection.IncomingFrameMaxSize);
        }

        internal virtual async ValueTask SendRequestFrameAsync(
            OutgoingRequest request,
            CancellationToken cancel = default)
        {
            var bufferWriter = new BufferWriter();
            bufferWriter.WriteByteSpan(TransportHeader.Span);
            if (IsIce1)
            {
                if (request.StreamParamSender != null)
                {
                    throw new NotSupportedException("stream parameters are not supported with ice1");
                }

                var encoder = new Ice11Encoder(bufferWriter);

                // Write the Ice1 request header.

                bufferWriter.WriteByteSpan(Ice1Definitions.FramePrologue);
                encoder.EncodeIce1FrameType(Ice1FrameType.Request);
                encoder.EncodeByte(0); // compression status
                BufferWriter.Position frameSizeStart = encoder.StartFixedLengthSize();

                // Note: we don't write the request ID here because the stream ID is not allocated yet. It
                // will be written once the stream ID is allocated from the send queue to ensure requests are
                // sent in the same order as the request ID values.
                encoder.EncodeInt(0);

                (byte encodingMajor, byte encodingMinor) = request.PayloadEncoding.ToMajorMinor();

                var requestHeader = new Ice1RequestHeader(
                    IdentityAndFacet.FromPath(request.Path),
                    request.Operation,
                    request.IsIdempotent ? OperationMode.Idempotent : OperationMode.Normal,
                    request.Features.GetContext(),
                    encapsulationSize: request.PayloadSize + 6,
                    encodingMajor,
                    encodingMinor);
                requestHeader.Encode(encoder);

                encoder.EncodeFixedLengthSize(bufferWriter.Size + request.PayloadSize, frameSizeStart);
            }
            else
            {
                var encoder = new Ice20Encoder(bufferWriter);

                // Write the Ice2 request header.

                encoder.EncodeIce2FrameType(Ice2FrameType.Request);

                // TODO: simplify sizes, we should be able to remove one of the sizes (the frame size, the
                // frame header size).
                BufferWriter.Position frameSizeStart = encoder.StartFixedLengthSize();
                BufferWriter.Position frameHeaderStart = encoder.StartFixedLengthSize(2);

                // DateTime.MaxValue represents an infinite deadline and it is encoded as -1
                long deadline = request.Deadline == DateTime.MaxValue ? -1 :
                        (long)(request.Deadline - DateTime.UnixEpoch).TotalMilliseconds;

                var requestHeaderBody = new Ice2RequestHeaderBody(
                    request.Path,
                    request.Operation,
                    request.IsIdempotent ? true : null,
                    priority: null,
                    deadline,
                    request.PayloadEncoding == Ice2Definitions.Encoding ? null : request.PayloadEncoding.ToString());

                requestHeaderBody.Encode(encoder);

                IDictionary<string, string> context = request.Features.GetContext();
                if (request.FieldsDefaults.ContainsKey((int)Ice2FieldKey.Context) || context.Count > 0)
                {
                    // Encodes context
                    request.Fields[(int)Ice2FieldKey.Context] =
                        encoder => encoder.EncodeDictionary(context,
                                                            (encoder, value) => encoder.EncodeString(value),
                                                            (encoder, value) => encoder.EncodeString(value));
                }
                // else context remains empty (not set)

                encoder.EncodeFields(request.Fields, request.FieldsDefaults);
                encoder.EncodeSize(request.PayloadSize);

                // We're done with the header encoding, write the header size.
                int headerSize = encoder.EndFixedLengthSize(frameHeaderStart, 2);

                // We're done with the frame encoding, write the frame size.
                int frameSize = headerSize + 2 + request.PayloadSize;
                encoder.EncodeFixedLengthSize(frameSize, frameSizeStart);
                if (frameSize > _connection.PeerIncomingFrameMaxSize)
                {
                    throw new ArgumentException(
                        $@"the request size ({frameSize} bytes) is larger than the peer's IncomingFrameMaxSize ({
                        _connection.PeerIncomingFrameMaxSize} bytes)",
                        nameof(request));
                }
            }

            // Add the payload to the buffer writer.
            bufferWriter.Add(request.Payload);

            // Send the request frame.
            await SendAsync(bufferWriter.Finish(),
                            endStream: request.StreamParamSender == null,
                            cancel).ConfigureAwait(false);

            // If there's a stream param sender, we can start sending the data.
            if (request.StreamParamSender != null)
            {
                request.SendStreamParam(this);
            }
        }

        internal virtual async ValueTask SendResponseFrameAsync(
            OutgoingResponse response,
            CancellationToken cancel = default)
        {
            var bufferWriter = new BufferWriter();
            bufferWriter.WriteByteSpan(TransportHeader.Span);
            if (IsIce1)
            {
                if (response.StreamParamSender != null)
                {
                    throw new NotSupportedException("stream parameters are not supported with ice1");
                }

                var encoder = new Ice11Encoder(bufferWriter);

                // Write the Ice1 response header.

                bufferWriter.WriteByteSpan(Ice1Definitions.FramePrologue);
                encoder.EncodeIce1FrameType(Ice1FrameType.Reply);
                encoder.EncodeByte(0); // compression status
                BufferWriter.Position frameSizeStart = encoder.StartFixedLengthSize();

                encoder.EncodeInt(RequestId);
                encoder.EncodeReplyStatus(response.ReplyStatus);
                if (response.ReplyStatus <= ReplyStatus.UserException)
                {
                    (byte encodingMajor, byte encodingMinor) = response.PayloadEncoding.ToMajorMinor();

                    var responseHeader = new Ice1ResponseHeader(encapsulationSize: response.PayloadSize + 6,
                                                                encodingMajor,
                                                                encodingMinor);
                    responseHeader.Encode(encoder);
                }

                encoder.EncodeFixedLengthSize(bufferWriter.Size + response.PayloadSize, frameSizeStart);
            }
            else
            {
                var encoder = new Ice20Encoder(bufferWriter);

                // Write the Ice2 response header.

                encoder.EncodeIce2FrameType(Ice2FrameType.Response);

                // TODO: simplify sizes, we should be able to remove one of the sizes (the frame size or the
                // frame header size).
                BufferWriter.Position frameSizeStart = encoder.StartFixedLengthSize();
                BufferWriter.Position frameHeaderStart = encoder.StartFixedLengthSize(2);

                new Ice2ResponseHeaderBody(
                    response.ResultType,
                    response.PayloadEncoding == Ice2Definitions.Encoding ? null :
                        response.PayloadEncoding.ToString()).Encode(encoder);

                encoder.EncodeFields(response.Fields, response.FieldsDefaults);
                encoder.EncodeSize(response.PayloadSize);

                // We're done with the header encoding, write the header size.
                int headerSize = encoder.EndFixedLengthSize(frameHeaderStart, 2);

                // We're done with the frame encoding, write the frame size.
                int frameSize = headerSize + 2 + response.PayloadSize;
                encoder.EncodeFixedLengthSize(frameSize, frameSizeStart);
                if (frameSize > _connection.PeerIncomingFrameMaxSize)
                {
                    // Throw a remote exception instead of this response, the Ice connection will catch it and send it
                    // as the response instead of sending this response which is too large.
                    throw new DispatchException(
                    $@"the response size ({frameSize} bytes) is larger than IncomingFrameMaxSize ({
                        _connection.PeerIncomingFrameMaxSize} bytes)");
                }
            }

            // Add the payload to the buffer writer.
            bufferWriter.Add(response.Payload);

            // Send the request frame.
            await SendAsync(bufferWriter.Finish(),
                            endStream: response.StreamParamSender == null,
                            cancel).ConfigureAwait(false);

            // If there's a stream param sender, we can start sending the data.
            if (response.StreamParamSender != null)
            {
                response.SendStreamParam(this);
            }
        }

        internal IDisposable? StartScope() => _connection.Logger.StartStreamScope(Id);

        private protected virtual ValueTask<ReadOnlyMemory<byte>> ReceiveIce1FrameAsync(
            Ice1FrameType expectedFrameType,
            CancellationToken cancel = default) =>
            // TODO: temporary until Ice1 protocol handler is implemented
            throw new NotSupportedException("ice1 protocol not supported by stream");

        private protected virtual async ValueTask<ReadOnlyMemory<byte>> ReceiveIce2FrameAsync(
            Ice2FrameType expectedFrameType,
            CancellationToken cancel)
        {
            Memory<byte> buffer = new byte[256];

            // Read the frame type and first byte of the size.
            await ReceiveFullAsync(buffer.Slice(0, 2), cancel).ConfigureAwait(false);
            var frameType = (Ice2FrameType)buffer.Span[0];
            if (frameType != expectedFrameType)
            {
                throw new InvalidDataException($"received frame type {frameType} but expected {expectedFrameType}");
            }

            // Read the remainder of the size if needed.
            int sizeLength = buffer.Span[1].DecodeSizeLength20();
            if (sizeLength > 1)
            {
                await ReceiveFullAsync(buffer.Slice(2, sizeLength - 1), cancel).ConfigureAwait(false);
            }

            int frameSize = buffer[1..].AsReadOnlySpan().DecodeSize20().Size;
            if (frameSize > _connection.IncomingFrameMaxSize)
            {
                throw new InvalidDataException(
                    $"frame with {frameSize} bytes exceeds IncomingFrameMaxSize connection option value");
            }

            if (frameSize > 0)
            {
                buffer = frameSize > buffer.Length ? new byte[frameSize] : buffer.Slice(0, frameSize);
                await ReceiveFullAsync(buffer, cancel).ConfigureAwait(false);
                return buffer;
            }
            else
            {
                return Memory<byte>.Empty;
            }
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
