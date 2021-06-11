// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame sent by the application.</summary>
    public sealed class OutgoingResponse : OutgoingFrame
    {
        /// <inheritdoc/>
        public override IReadOnlyDictionary<int, ReadOnlyMemory<byte>> InitialFields { get; } =
            ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; private protected set; }

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
        /// <param name="streamWriter">The stream writer to encode the stream parameter.</param>
        public OutgoingResponse(
            IncomingRequest request,
            IList<ArraySegment<byte>> payload,
            StreamWriter? streamWriter = null)
            : this(request.Protocol, payload, request.PayloadEncoding, FeatureCollection.Empty, streamWriter)
        {
            ResultType = ResultType.Success;
            ReplyStatus = ReplyStatus.OK;
        }

        /// <summary>Constructs an outgoing response with a payload. The new response will use the protocol
        /// of the <paramref name="dispatch"/> and corresponds to a successful completion.</summary>
        /// <param name="dispatch">The dispatch for which this constructor creates a response.</param>
        /// <param name="payload">The payload of this response encoded using dispatch.Encoding.</param>
        /// <param name="streamWriter">The writer to encode the stream parameter.</param>
        public OutgoingResponse(
            Dispatch dispatch,
            IList<ArraySegment<byte>> payload,
            StreamWriter? streamWriter = null)
            : this(dispatch.Protocol, payload, dispatch.Encoding, dispatch.ResponseFeatures, streamWriter)
        {
            ResultType = ResultType.Success;
            ReplyStatus = ReplyStatus.OK;
        }

        /// <summary>Constructs an outgoing response from the given incoming response. The new response will use the
        /// protocol of the <paramref name="request"/> and the encoding of <paramref name="response"/>.</summary>
        /// <param name="request">The request on which this constructor creates a response.</param>
        /// <param name="response">The incoming response used to construct the new outgoing response.</param>
        /// <param name="forwardFields">When true (the default), the new response uses the incoming response's fields as
        /// a fallback - all the fields in this field dictionary are added before the response is sent, except for
        /// fields previously added by middleware.</param>
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
            Payload = new List<ArraySegment<byte>>();

            ArraySegment<byte> incomingResponsePayload = response.Payload; // TODO: temporary

            if (Protocol == response.Protocol)
            {
                Payload.Add(incomingResponsePayload);

                if (Protocol == Protocol.Ice2 && forwardFields)
                {
                    // Don't forward RetryPolicy
                    InitialFields = response.Fields.ToImmutableDictionary().Remove((int)Ice2FieldKey.RetryPolicy);
                }
            }
            else
            {
                if (response.ResultType == ResultType.Failure && PayloadEncoding == Encoding.V11)
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
                        Payload.Add(incomingResponsePayload.Slice(1));
                    }
                    else
                    {
                        Debug.Assert(Protocol == Protocol.Ice2);
                        Debug.Assert(response.Protocol == Protocol.Ice1);

                        // Prepend a little buffer in front of the ice2 response payload to hold the reply status
                        byte[] buffer = new byte[1];
                        buffer[0] = (byte)ReplyStatus;
                        Payload.Add(buffer);
                        Payload.Add(incomingResponsePayload);
                    }
                }
                else
                {
                    Payload.Add(incomingResponsePayload);
                }
            }
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

                FieldsOverride.Add(
                    (int)Ice2FieldKey.RetryPolicy,
                    ostr =>
                    {
                        ostr.Write(retryPolicy.Retryable);
                        if (retryPolicy.Retryable == Retryable.AfterDelay)
                        {
                            ostr.WriteVarUInt((uint)retryPolicy.Delay.TotalMilliseconds);
                        }
                    });
            }
        }

        /// <inheritdoc/>
        internal override IncomingFrame ToIncoming() => new IncomingResponse(this);

        /// <inheritdoc/>
        internal override void WriteHeader(OutputStream ostr)
        {
            Debug.Assert(ostr.Encoding == Protocol.GetEncoding());

            if (Protocol == Protocol.Ice2)
            {
                OutputStream.Position startPos = ostr.StartFixedLengthSize(2);
                WriteFields(ostr);
                ostr.Write(ResultType);
                PayloadEncoding.IceWrite(ostr);
                ostr.WriteSize(PayloadSize);
                ostr.EndFixedLengthSize(startPos, 2);
            }
            else
            {
                Debug.Assert(Protocol == Protocol.Ice1);

                ostr.Write(ReplyStatus);
                if (ReplyStatus <= ReplyStatus.UserException)
                {
                    var responseHeader = new Ice1ResponseHeader(encapsulationSize: PayloadSize + 6, PayloadEncoding);
                    responseHeader.IceWrite(ostr);
                }
            }
        }

        private OutgoingResponse(
            Protocol protocol,
            IList<ArraySegment<byte>> payload,
            Encoding payloadEncoding,
            FeatureCollection features,
            StreamWriter? streamWriter)
            : base(protocol, features, streamWriter)
        {
            PayloadEncoding = payloadEncoding;
            Payload = payload;
        }
    }
}
