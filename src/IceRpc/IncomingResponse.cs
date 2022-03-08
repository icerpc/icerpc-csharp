// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame received by the application.</summary>
    public sealed class IncomingResponse : IncomingFrame
    {
        /// <summary>The request that received this response.</summary>
        public OutgoingRequest Request { get; }

        /// <summary>Gets or initializes the <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; init; } = ResultType.Success;

        /// <summary>Constructs an incoming response.</summary>
        /// <param name="request">The corresponding outgoing request.</param>
        public IncomingResponse(OutgoingRequest request)
            : base(request.Protocol) => Request = request;
    }
}
