// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Represents a response protocol frame sent by the application.</summary>
    public sealed class OutgoingResponse : OutgoingFrame
    {
        /// <summary>Returns the encoding of the payload of this response.</summary>
        public Encoding PayloadEncoding { get; init; } = Encoding.Unknown;

        /// <summary>The corresponding request.</summary>
        public IncomingRequest Request { get; }

        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; init; } = ResultType.Success;

        /// <summary>Constructs an outgoing response.</summary>
        /// <param name="request">The incoming request.</param>
        public OutgoingResponse(IncomingRequest request) :
            base(request.Protocol, request.ResponseWriter)
        {
            PayloadEncoding = request.PayloadEncoding;
            Request = request;
        }
    }
}
