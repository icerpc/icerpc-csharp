// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame received by the application.</summary>
    public sealed class IncomingResponse : IncomingFrame
    {
        /// <summary>The request that received this response.</summary>
        public OutgoingRequest Request { get; }

        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; }

        /// <summary>Constructs an incoming response.</summary>
        /// <param name="request">The corresponding outgoing request.</param>
        /// <param name="resultType">The <see cref="IceRpc.ResultType"/> of the response.</param>
        /// <param name="payload">The payload of the response.</param>
        internal IncomingResponse(
            OutgoingRequest request,
            ResultType resultType,
            PipeReader payload) :
            base(request.Protocol, payload)
        {
            Request = request;
            ResultType = resultType;
        }
    }
}
