// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame sent by the application.</summary>
    public sealed class OutgoingResponse : OutgoingFrame
    {
        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; }

        /// <summary>The <see cref="IceRpc.ReplyStatus"/> of this response.</summary>
        /// <value><see cref="IceRpc.ReplyStatus.OK"/> when <see cref="ResultType"/> is
        /// <see cref="IceRpc.ResultType.Success"/>; otherwise, if <see cref="PayloadEncoding"/> is 1.1, the value is
        /// stored in the response header or payload. For any other payload encoding, the value is
        /// <see cref="IceRpc.ReplyStatus.UserException"/>.</value>
        public ReplyStatus ReplyStatus { get; }

        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; }

        /// <summary>Constructs an outgoing response with the given payload. The new response will use the protocol and
        /// encoding of <paramref name="request"/> and corresponds to a successful completion.</summary>
        /// <param name="request">The request for which this constructor creates a response.</param>
        /// <param name="payload">The payload of this response encoded using request.PayloadEncoding.</param>
        /// <param name="streamParamSender">The stream param sender.</param>
        public OutgoingResponse(
            IncomingRequest request,
            ReadOnlyMemory<ReadOnlyMemory<byte>> payload,
            IStreamParamSender? streamParamSender = null)
            : this(request.Protocol, payload, request.PayloadEncoding, FeatureCollection.Empty, streamParamSender)
        {
            ResultType = ResultType.Success;
            ReplyStatus = ReplyStatus.OK;
        }

        /// <summary>Constructs an outgoing response with a payload. The new response will use the protocol
        /// of the <paramref name="dispatch"/> and corresponds to a successful completion.</summary>
        /// <param name="dispatch">The dispatch for which this constructor creates a response.</param>
        /// <param name="payload">The payload of this response encoded using <c>dispatch.Encoding</c>.</param>
        /// <param name="streamParamSender">The stream param sender.</param>
        public OutgoingResponse(
            Dispatch dispatch,
            ReadOnlyMemory<ReadOnlyMemory<byte>> payload,
            IStreamParamSender? streamParamSender = null)
            : this(dispatch.Protocol, payload, dispatch.Encoding, dispatch.ResponseFeatures, streamParamSender)
        {
            ResultType = ResultType.Success;
            ReplyStatus = ReplyStatus.OK;
        }

        /// <summary>Constructs an outgoing response from the given incoming response. The new response will use the
        /// protocol of the <paramref name="request"/> and the encoding of <paramref name="response"/>.</summary>
        /// <param name="request">The request on which this constructor creates a response.</param>
        /// <param name="response">The incoming response used to construct the new outgoing response.</param>
        /// <param name="forwardFields">When true (the default), the new response uses the incoming response's fields as
        /// defaults for its fields.</param>
            // TODO: support stream param forwarding
        public OutgoingResponse(
            IncomingRequest request,
            IncomingResponse response,
            bool forwardFields = true)
            : base(request.Protocol, FeatureCollection.Empty, null)
        {
            ResultType = response.ResultType;
            ReplyStatus = response.ReplyStatus;

            PayloadEncoding = response.PayloadEncoding;
            var payload = new List<ReadOnlyMemory<byte>>();

            ReadOnlyMemory<byte> incomingResponsePayload = response.Payload; // TODO: temporary

            if (Protocol == response.Protocol)
            {
                payload.Add(incomingResponsePayload);

                if (Protocol == Protocol.Ice2 && forwardFields)
                {
                    // Don't forward RetryPolicy
                    FieldsDefaults = response.Fields.ToImmutableDictionary().Remove((int)Ice2FieldKey.RetryPolicy);
                }
            }
            else
            {
                if (response.ResultType == ResultType.Failure && PayloadEncoding == Encoding.Ice11)
                {
                    // When the response carries a failure encoded with 1.1, we need to perform a small adjustment
                    // between ice1 and ice2 response frames.
                    // ice1: [failure reply status][payload size, encoding and bytes|special exception]
                    // ice2: [failure result type][payload encoding][payload size][reply status][payload bytes|
                    //                                                                          special exception bytes]
                    // There is no such adjustment with other encoding, or when the response does not carry a failure.

                    if (Protocol == Protocol.Ice1)
                    {
                        Debug.Assert(response.Protocol == Protocol.Ice2);

                        // We slice-off the reply status that is part of the ice2 payload.
                        payload.Add(incomingResponsePayload[1..]);
                    }
                    else
                    {
                        Debug.Assert(Protocol == Protocol.Ice2);
                        Debug.Assert(response.Protocol == Protocol.Ice1);

                        // Prepend a little buffer in front of the ice2 response payload to hold the reply status
                        // TODO: we don't want little buffers!
                        byte[] buffer = new byte[1];
                        buffer[0] = (byte)ReplyStatus;
                        payload.Add(buffer);
                        payload.Add(incomingResponsePayload);
                    }
                }
                else
                {
                    payload.Add(incomingResponsePayload);
                }
            }
            Payload = payload.ToArray();
        }

        /// <summary>Constructs a response that represents a failure and contains an exception.</summary>
        /// <param name="request">The incoming request for which this constructor
        ///  creates a response.</param>
        /// <param name="exception">The exception to store into the response's payload.</param>
        public OutgoingResponse(IncomingRequest request, RemoteException exception)
            : base(request.Protocol, exception.Features, null)
        {
            ResultType = ResultType.Failure;
            PayloadEncoding = request.PayloadEncoding;
            (Payload, ReplyStatus) = IceRpc.Payload.FromRemoteException(request, exception);

            if (Protocol == Protocol.Ice2 && exception.RetryPolicy.Retryable != Retryable.No)
            {
                RetryPolicy retryPolicy = exception.RetryPolicy;

                Fields.Add(
                    (int)Ice2FieldKey.RetryPolicy,
                    encoder =>
                    {
                        encoder.EncodeRetryable(retryPolicy.Retryable);
                        if (retryPolicy.Retryable == Retryable.AfterDelay)
                        {
                            encoder.EncodeVarUInt((uint)retryPolicy.Delay.TotalMilliseconds);
                        }
                    });
            }
        }

        /// <inheritdoc/>
        internal override IncomingFrame ToIncoming() => new IncomingResponse(this);

        /// <inheritdoc/>
        internal override void EncodeHeader(IceEncoder encoder)
        {
            Debug.Assert(encoder.Encoding == Protocol.GetEncoding());

            if (Protocol == Protocol.Ice2)
            {
                BufferWriter.Position startPos = encoder.StartFixedLengthSize(2);
                new Ice2ResponseHeaderBody(
                    ResultType,
                    PayloadEncoding == Ice2Definitions.Encoding ? null : PayloadEncoding.ToString()).Encode(encoder);
                EncodeFields(encoder);
                encoder.EncodeSize(PayloadSize);
                encoder.EndFixedLengthSize(startPos, 2);
            }
            else
            {
                Debug.Assert(Protocol == Protocol.Ice1);

                encoder.EncodeReplyStatus(ReplyStatus);
                if (ReplyStatus <= ReplyStatus.UserException)
                {
                    (byte encodingMajor, byte encodingMinor) = PayloadEncoding.ToMajorMinor();

                    var responseHeader = new Ice1ResponseHeader(encapsulationSize: PayloadSize + 6,
                                                                encodingMajor,
                                                                encodingMinor);
                    responseHeader.Encode(encoder);
                }
            }
        }

        private OutgoingResponse(
            Protocol protocol,
            ReadOnlyMemory<ReadOnlyMemory<byte>> payload,
            Encoding payloadEncoding,
            FeatureCollection features,
            IStreamParamSender? streamParamSender)
            : base(protocol, features, streamParamSender)
        {
            PayloadEncoding = payloadEncoding;
            Payload = payload;
        }
    }
}
