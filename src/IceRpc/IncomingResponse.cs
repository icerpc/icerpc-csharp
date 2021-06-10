// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame received by the application.</summary>
    public sealed class IncomingResponse : IncomingFrame
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

        /// <summary>Constructs an incoming response frame.</summary>
        /// <param name="protocol">The protocol of this response</param>
        /// <param name="data">The frame data as an array segment.</param>
        internal IncomingResponse(Protocol protocol, ArraySegment<byte> data)
            : base(protocol)
        {
            var istr = new InputStream(data, Protocol.GetEncoding());
            if (Protocol == Protocol.Ice1)
            {
                ReplyStatus = istr.ReadReplyStatus();
                ResultType = ReplyStatus == ReplyStatus.OK ? ResultType.Success : ResultType.Failure;

                if (ReplyStatus <= ReplyStatus.UserException)
                {
                    var responseHeader = new Ice1ResponseHeader(istr);
                    PayloadEncoding = responseHeader.PayloadEncoding;
                    ArraySegment<byte> payload = data.Slice(istr.Pos);

                    int payloadSize = responseHeader.EncapsulationSize - 6;
                    if (payloadSize != payload.Count)
                    {
                        throw new InvalidDataException(
                            @$"response payload size mismatch: expected {payloadSize} bytes, read {payload.Count
                            } bytes");
                    }
                    Payload = payload;
                }
                else
                {
                    // "special" exception
                    PayloadEncoding = Encoding.V11;
                    Payload = data.Slice(istr.Pos);
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
                ArraySegment<byte> payload = data.Slice(istr.Pos);
                if (payloadSize != payload.Count)
                {
                    throw new InvalidDataException(
                        $"response payload size mismatch: expected {payloadSize} bytes, read {payload.Count} bytes");
                }
                Payload = payload;

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
            Payload = response.Payload.ToArraySegment();
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
                retryPolicy = value.ReadFieldValue(istr => new RetryPolicy(istr));
            }
            return retryPolicy;
        }
    }
}
