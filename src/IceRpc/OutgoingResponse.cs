// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame sent by the application.</summary>
    public sealed class OutgoingResponse : OutgoingFrame
    {
        /// <summary>The <see cref="ReplyStatus"/> of this response.</summary>
        /// <value><see cref="ReplyStatus.OK"/> when <see cref="ResultType"/> is <see
        /// cref="ResultType.Success"/>; otherwise, if <see cref="OutgoingFrame.PayloadEncoding"/> is 1.1, the
        /// value is stored in the response header or payload. For any other payload encoding, the value is
        /// <see cref="ReplyStatus.UserException"/>.</value>
        public ReplyStatus ReplyStatus { get; }

        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; }

        /// <summary>Constructs an outgoing response.</summary>
        /// <param name="protocol">The <see cref="Protocol"/> used to send the response.</param>
        /// <param name="resultType">The <see cref="ResultType"/> of the response.</param>
        /// <param name="replyStatus">The <see cref="ReplyStatus"/> of the response.</param>
        public OutgoingResponse(Protocol protocol, ResultType resultType, ReplyStatus? replyStatus = null) :
            base(protocol)
        {
            ReplyStatus = replyStatus ?? (resultType == ResultType.Success ? ReplyStatus.OK : ReplyStatus.UserException);
            ResultType = resultType;
        }

        /// <summary>Constructs a successful response that contains a payload.</summary>
        /// <param name="request">The incoming request for which this method creates a response.</param>
        /// <param name="payload">The exception to store into the response's payload.</param>
        /// <returns>The outgoing response.</returns>
        public static OutgoingResponse ForPayload(
            IncomingRequest request,
            ReadOnlyMemory<ReadOnlyMemory<byte>> payload) =>
            new(request.Protocol, ResultType.Success)
            {
                Payload = payload,
                PayloadEncoding = request.PayloadEncoding,
            };

        /// <summary>Constructs a failure response that contains an exception.</summary>
        /// <param name="request">The incoming request for which this method creates a response.</param>
        /// <param name="exception">The exception to store into the response's payload.</param>
        /// <returns>The outgoing response.</returns>
        public static OutgoingResponse ForRemoteException(IncomingRequest request, RemoteException exception)
        {
            (ReadOnlyMemory<ReadOnlyMemory<byte>> payload, ReplyStatus replyStatus) =
                IceRpc.Payload.FromRemoteException(request, exception);

            var response = new OutgoingResponse(request.Protocol, ResultType.Failure, replyStatus)
            {
                Payload = payload,
                PayloadEncoding = request.PayloadEncoding,
            };

            RetryPolicy retryPolicy = exception.RetryPolicy;
            if (retryPolicy.Retryable != Retryable.No && response.Protocol == Protocol.Ice2)
            {
                response.Fields.Add(
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

            return response;
        }

        /// <summary>Returns a new incoming response built from this outgoing response. This method is
        /// used for colocated calls.</summary>
        internal IncomingResponse ToIncoming() =>
            new(Protocol, ResultType, ReplyStatus)
            {
                Fields = GetAllFields(),
                PayloadEncoding = PayloadEncoding,
                Payload = Payload.ToSingleBuffer(),
            };
    }
}
