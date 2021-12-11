// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Represents a response protocol frame sent by the application.</summary>
    public sealed class OutgoingResponse : OutgoingFrame
    {
        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; init; } = ResultType.Success;

        /// <summary>Constructs an outgoing response.</summary>
        /// <param name="request">The incoming request.</param>
        public OutgoingResponse(IncomingRequest request) :
            base(request.Protocol, request.InitialResponsePayloadSink) =>
            PayloadEncoding = request.PayloadEncoding;
    }
}
