// Copyright (c) ZeroC, Inc. All rights reserved.

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

        /// <summary>Constructs a successful response with the specified payload.</summary>
        /// <param name="request">The incoming request.</param>
        /// <param name="payload">The response's payload.</param>
        public OutgoingResponse(IncomingRequest request, ReadOnlyMemory<ReadOnlyMemory<byte>> payload) :
            this(request.Protocol, ResultType.Success)
        {
            Payload = payload;
            PayloadEncoding = request.PayloadEncoding;
        }
    }
}
