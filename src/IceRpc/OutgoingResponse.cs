// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame sent by the application.</summary>
    public sealed class OutgoingResponse : OutgoingFrame
    {
        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; }

        /// <summary>Constructs an outgoing response.</summary>
        /// <param name="protocol">The <see cref="Protocol"/> used to send the response.</param>
        /// <param name="resultType">The <see cref="ResultType"/> of the response.</param>
        public OutgoingResponse(Protocol protocol, ResultType resultType) :
            base(protocol) => ResultType = resultType;

        /// <summary>Constructs a successful response that contains a payload.</summary>
        /// <param name="request">The incoming request for which this method creates a response.</param>
        /// <param name="payload">The response's payload.</param>
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
        public static OutgoingResponse ForException(IncomingRequest request, Exception exception) =>
            request.Protocol.CreateResponseFromException(exception, request);
    }
}
