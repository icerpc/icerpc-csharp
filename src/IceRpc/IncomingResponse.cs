// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame received by the application.</summary>
    public sealed class IncomingResponse : IncomingFrame, IDisposable
    {
        /// <inheritdoc/>
        public override IReadOnlyDictionary<int, ReadOnlyMemory<byte>> Fields { get; } =
            ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; private protected set; }

        /// <summary>The <see cref="IceRpc.ReplyStatus"/> of this response.</summary>
        /// <value><see cref="IceRpc.ReplyStatus.OK"/> when <see cref="ResultType"/> is
        /// <see cref="IceRpc.ResultType.Success"/>; otherwise, if <see cref="PayloadEncoding"/> is 1.1, the value is
        /// read from the response header or payload. For any other payload encoding, the value is
        /// <see cref="IceRpc.ReplyStatus.UserException"/>.</value>
        public ReplyStatus ReplyStatus { get; }

        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; }

        // The optional socket stream. The stream is non-null if there's still data to read over the stream
        // after the reading of the response frame.
        internal SocketStream? SocketStream { get; set; }

        /// <summary>Constructs an incoming response frame.</summary>
        /// <param name="protocol">The protocol of the response.</param>
        /// <param name="data">The frame data as an array segment.</param>
        public IncomingResponse(Protocol protocol, ArraySegment<byte> data)
            : this(protocol, data, null)
        {
        }

        /// <summary>Releases resources used by the response frame.</summary>
        public void Dispose() => SocketStream?.Release();

        /*
        /// <summary>Reads the return value which contains a stream return value. If this response frame carries a
        /// failure, reads and throws this exception.</summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="proxy">The proxy used to send the request.</param>
        /// <param name="reader">A reader used to read the frame return value, when the frame return value contain
        /// multiple values the reader must use a tuple to return the values.</param>
        /// <returns>The frame return value.</returns>
        public T ReadReturnValue<T>(IServicePrx proxy, InputStreamReaderWithStreamable<T> reader)
        {
            if (ResultType == ResultType.Success)
            {
                if (SocketStream == null)
                {
                    throw new InvalidDataException("no stream data available for operation with stream parameter");
                }

                var istr = new InputStream(Payload.AsReadOnlyMemory(1),
                                           Protocol.GetEncoding(),
                                           Connection,
                                           proxy.GetOptions(),
                                           startEncapsulation: true);
                T value = reader(istr, SocketStream);
                // Clear the socket stream to ensure it's not disposed with the response frame. It's now the
                // responsibility of the stream parameter object to dispose the socket stream.
                SocketStream = null;
                istr.CheckEndOfBuffer(skipTaggedParams: true);
                return value;
            }
            else
            {
                if (SocketStream != null)
                {
                    throw new InvalidDataException("stream data available with remote exception result");
                }
                throw ReadException(proxy);
            }
        }

        /// <summary>Reads the return value which is a stream return value only. If this response frame carries a
        /// failure, reads and throws this exception.</summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="proxy">The proxy used to send the request.</param>
        /// <param name="reader">A reader used to read the frame return value.</param>
        /// <returns>The frame return value.</returns>
        public T ReadReturnValue<T>(IServicePrx proxy, Func<SocketStream, T> reader)
        {
            if (ResultType == ResultType.Success)
            {
                if (SocketStream == null)
                {
                    throw new InvalidDataException("no stream data available for operation with stream parameter");
                }
                Payload.AsReadOnlyMemory(1).ReadEmptyEncapsulation(Protocol.GetEncoding());
                T value = reader(SocketStream);
                // Clear the socket stream to ensure it's not disposed with the response frame. It's now the
                // responsibility of the stream parameter object to dispose the socket stream.
                SocketStream = null;
                return value;
            }
            else
            {
                if (SocketStream != null)
                {
                    throw new InvalidDataException("stream data available with remote exception result");
                }
                throw ReadException(proxy);
            }
        }
        */

        /// <summary>Constructs an incoming response frame.</summary>
        /// <param name="protocol">The protocol of this response</param>
        /// <param name="data">The frame data as an array segment.</param>
        /// <param name="socketStream">The optional socket stream. The stream is non-null if there's still data to
        /// read on the stream after the reading the response frame.</param>
        internal IncomingResponse(
            Protocol protocol,
            ArraySegment<byte> data,
            SocketStream? socketStream)
            : base(protocol)
        {
            SocketStream = socketStream;

            var istr = new InputStream(data, Protocol.GetEncoding());
            if (Protocol == Protocol.Ice1)
            {
                ReplyStatus = istr.ReadReplyStatus();
                ResultType = ReplyStatus == ReplyStatus.OK ? ResultType.Success : ResultType.Failure;

                if (ReplyStatus <= ReplyStatus.UserException)
                {
                    var responseHeader = new Ice1ResponseHeader(istr);
                    PayloadEncoding = responseHeader.PayloadEncoding;
                    Payload = data.Slice(istr.Pos);

                    int payloadSize = responseHeader.EncapsulationSize - 6;
                    if (payloadSize != Payload.Count)
                    {
                        throw new InvalidDataException(
                            @$"response payload size mismatch: expected {payloadSize} bytes, read {Payload.Count
                            } bytes");
                    }
                }
                else
                {
                    // "special" exception
                    Payload = data.Slice(istr.Pos);
                    PayloadEncoding = Encoding.V11;
                }
            }
            else
            {
                Debug.Assert(Protocol == Protocol.Ice2);
                int headerSize = istr.ReadSize();
                int startPos = istr.Pos;
                Fields = istr.ReadFieldDictionary();
                ResultType = istr.ReadResultType();
                PayloadEncoding = new Encoding(istr);

                int payloadSize = istr.ReadSize();
                if (istr.Pos - startPos != headerSize)
                {
                    throw new InvalidDataException(
                        @$"received invalid response header: expected {headerSize} bytes but read {istr.Pos - startPos
                        } bytes");
                }
                Payload = data.Slice(istr.Pos);
                if (payloadSize != Payload.Count)
                {
                    throw new InvalidDataException(
                        $"response payload size mismatch: expected {payloadSize} bytes, read {Payload.Count} bytes");
                }

                if (ResultType == ResultType.Failure && PayloadEncoding == Encoding.V11)
                {
                    ReplyStatus = istr.ReadReplyStatus(); // first byte of the payload
                }
                else
                {
                    ReplyStatus = ResultType == ResultType.Success ? ReplyStatus.OK : ReplyStatus.UserException;
                }
            }
        }

        /// <summary>Constructs an incoming response frame from an outgoing response frame. Used for colocated calls.
        /// </summary>
        /// <param name="response">The outgoing response frame.</param>
        internal IncomingResponse(OutgoingResponse response)
            : base(response.Protocol)
        {
            if (Protocol == Protocol.Ice2)
            {
                Fields = response.GetFields();
            }

            ResultType = response.ResultType;
            ReplyStatus = response.ReplyStatus;

            PayloadEncoding = response.PayloadEncoding;
            Payload = response.Payload.AsArraySegment();
        }

        // Constructor for oneway response pseudo frame.
        internal IncomingResponse(Connection connection, Encoding encoding)
            : base(connection.Protocol)
        {
            Connection = connection;

            ResultType = ResultType.Success;
            ReplyStatus = ReplyStatus.OK;

            PayloadEncoding = encoding;
            Payload = Protocol.GetVoidReturnPayload(encoding);
        }

        internal RetryPolicy GetRetryPolicy(ServicePrx proxy)
        {
            RetryPolicy retryPolicy = RetryPolicy.NoRetry;
            if (PayloadEncoding == Encoding.V11)
            {
                // For compatibility with ZeroC Ice
                if (ReplyStatus == ReplyStatus.ObjectNotExistException && proxy.IsIndirect)
                {
                    retryPolicy = RetryPolicy.OtherReplica;
                }
            }
            else if (Fields.TryGetValue((int)Ice2FieldKey.RetryPolicy, out ReadOnlyMemory<byte> value))
            {
                retryPolicy = value.Read(istr => new RetryPolicy(istr));
            }
            return retryPolicy;
        }
    }
}
