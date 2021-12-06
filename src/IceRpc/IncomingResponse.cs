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
        /// <param name="payloadReader">The payload reader of the response.</param>
        /// <param name="payloadEncoding">The encoding of the payload.</param>
        public IncomingResponse(
            Protocol protocol,
            ResultType resultType,
            PipeReader payloadReader,
            Encoding payloadEncoding) :
            base(protocol, payloadReader, payloadEncoding) => ResultType = resultType;
    }
}
