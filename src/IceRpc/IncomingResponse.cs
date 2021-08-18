// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
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
        public override Encoding PayloadEncoding { get; }

        /// <summary>The <see cref="IceRpc.ReplyStatus"/> of this response.</summary>
        /// <value><see cref="ReplyStatus.OK"/> when <see cref="ResultType"/> is
        /// <see cref="ResultType.Success"/>; otherwise, if <see cref="PayloadEncoding"/> is 1.1, the value is
        /// read from the response header or payload. For any other payload encoding, the value is
        /// <see cref="ReplyStatus.UserException"/>.</value>
        public ReplyStatus ReplyStatus { get; }

        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; }

        /// <summary>Constructs an incoming response frame.</summary>
        /// <param name="protocol">The protocol of this response</param>
        /// <param name="data">The frame data.</param>
        internal IncomingResponse(Protocol protocol, ReadOnlyMemory<byte> data)
            : base(protocol)
        {
            if (Protocol == Protocol.Ice1)
            {
                var decoder = new Ice11Decoder(data);
                ReplyStatus = decoder.DecodeReplyStatus();
                ResultType = ReplyStatus == ReplyStatus.OK ? ResultType.Success : ResultType.Failure;

                if (ReplyStatus <= ReplyStatus.UserException)
                {
                    var responseHeader = new Ice1ResponseHeader(decoder);
                    PayloadEncoding = Encoding.FromMajorMinor(responseHeader.PayloadEncodingMajor,
                                                              responseHeader.PayloadEncodingMinor);
                    Payload = data[decoder.Pos..];

                    int payloadSize = responseHeader.EncapsulationSize - 6;
                    if (payloadSize != Payload.Length)
                    {
                        throw new InvalidDataException(
                            @$"response payload size mismatch: expected {payloadSize} bytes, read {Payload.Length
                            } bytes");
                    }
                }
                else
                {
                    // "special" exception
                    PayloadEncoding = Encoding.Ice11;
                    Payload = data[decoder.Pos..];
                }
            }
            else
            {
                var decoder = new Ice20Decoder(data);
                Debug.Assert(Protocol == Protocol.Ice2);
                int headerSize = decoder.DecodeSize();
                int startPos = decoder.Pos;
                var responseHeaderBody = new Ice2ResponseHeaderBody(decoder);
                ResultType = responseHeaderBody.ResultType;
                PayloadEncoding = responseHeaderBody.PayloadEncoding is string payloadEncoding ?
                    Encoding.FromString(payloadEncoding) : Ice2Definitions.Encoding;

                Fields = decoder.DecodeFieldDictionary();

                int payloadSize = decoder.DecodeSize();
                if (decoder.Pos - startPos != headerSize)
                {
                    throw new InvalidDataException(
                        @$"received invalid response header: expected {headerSize} bytes but read {decoder.Pos - startPos
                        } bytes");
                }
                Payload = data[decoder.Pos..];
                if (payloadSize != Payload.Length)
                {
                    throw new InvalidDataException(
                        $"response payload size mismatch: expected {payloadSize} bytes, read {Payload.Length} bytes");
                }

                if (ResultType == ResultType.Failure && PayloadEncoding == Encoding.Ice11)
                {
                    ReplyStatus = decoder.DecodeReplyStatus(); // first byte of the payload
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
                Fields = response.GetAllFields();
            }

            ResultType = response.ResultType;
            ReplyStatus = response.ReplyStatus;

            PayloadEncoding = response.PayloadEncoding;
            Payload = response.Payload.ToSingleBuffer();
        }

        // Constructor for oneway response pseudo frame.
        internal IncomingResponse(Connection connection, Encoding encoding)
            : base(connection.Protocol)
        {
            Connection = connection;

            ResultType = ResultType.Success;
            ReplyStatus = ReplyStatus.OK;

            PayloadEncoding = encoding;
            Payload = Protocol.GetVoidReturnPayload(PayloadEncoding);
        }

        internal RetryPolicy GetRetryPolicy(Proxy proxy)
        {
            RetryPolicy retryPolicy = RetryPolicy.NoRetry;
            if (PayloadEncoding == Encoding.Ice11)
            {
                // For compatibility with ZeroC Ice
                if (ReplyStatus == ReplyStatus.ObjectNotExistException &&
                    proxy.Protocol == Protocol.Ice1 &&
                    (proxy.Endpoint == null || proxy.Endpoint.Transport == TransportNames.Loc)) // "indirect" proxy
                {
                    retryPolicy = RetryPolicy.OtherReplica;
                }
            }
            else if (Fields.TryGetValue((int)Ice2FieldKey.RetryPolicy, out ReadOnlyMemory<byte> value))
            {
                retryPolicy = Ice20Decoder.DecodeFieldValue(value, decoder => new RetryPolicy(decoder));
            }
            return retryPolicy;
        }
    }
}
