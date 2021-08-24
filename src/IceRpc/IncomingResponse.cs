// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame received by the application.</summary>
    public sealed class IncomingResponse : IncomingFrame
    {
        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; init; }

        /// <summary>Constructs an incoming response.</summary>
        /// <param name="protocol">The <see cref="Protocol"/> used to receive the response.</param>
        /// <param name="resultType">The <see cref="ResultType"/> of the response.</param>
        public IncomingResponse(Protocol protocol, ResultType resultType) :
            base(protocol) => ResultType = resultType;

        /// <summary>Reads the exception from the failed response.</summary>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <returns>The remote exception.</returns>
        public Exception ToException(IInvoker? invoker)
        {
            if (PayloadSize == 0 || ResultType == ResultType.Success)
            {
                throw new InvalidOperationException("payload does not carry a remote exception");
            }
            return Protocol.DecodeResponseException(this, invoker);
        }

        /// <summary>Create an outgoing response from this incoming response. The response is constructed to be
        /// forwarded using the given target protocol.</summary>
        /// <param name="targetProtocol">The protocol used to send to the outgoing response.</param>
        /// <returns>The outgoing response to be forwarded.</returns>
        public OutgoingResponse ToOutgoingResponse(Protocol targetProtocol) => targetProtocol.ToOutgoingResponse(this);
    }
}
