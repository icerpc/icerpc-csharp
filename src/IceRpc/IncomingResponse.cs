// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame received by the application.</summary>
    public sealed class IncomingResponse : IncomingFrame
    {
        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; }

        /// <summary>Constructs an incoming response.</summary>
        /// <param name="protocol">The <see cref="Protocol"/> used to receive the response.</param>
        /// <param name="resultType">The <see cref="ResultType"/> of the response.</param>
        /// <param name="payload">The payload of the response.</param>
        /// <param name="payloadEncoding">The encoding of the payload.</param>
        internal IncomingResponse(
            Protocol protocol,
            ResultType resultType,
            PipeReader payload,
            Encoding payloadEncoding) :
            base(protocol, payload, payloadEncoding) => ResultType = resultType;
    }
}
