// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Collections.Immutable;
using System.Diagnostics;

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
        public IncomingResponse(Protocol protocol, ResultType resultType) :
            base(protocol) => ResultType = resultType;

        /// <summary>Create an outgoing response from this incoming response. The response is constructed to be
        /// forwarded using the given target protocol.</summary>
        /// <param name="targetProtocol">The protocol used to send to the outgoing response.</param>
        /// <returns>The outgoing response to be forwarded.</returns>
        public OutgoingResponse ToOutgoingResponse(Protocol targetProtocol)
        {
            FeatureCollection features = FeatureCollection.Empty;
            if (ResultType == ResultType.Failure && targetProtocol == Protocol.Ice1)
            {
                features = new FeatureCollection();
                ReplyStatus replyStatus = Features.Get<ReplyStatus>();
                features.Set(replyStatus > ReplyStatus.OK ? replyStatus : ReplyStatus.UserException);
            }

            return new OutgoingResponse(targetProtocol, ResultType)
            {
                Features = features,
                // Don't forward RetryPolicy
                FieldsDefaults = Fields.ToImmutableDictionary().Remove((int)FieldKey.RetryPolicy),
                Payload = new ReadOnlyMemory<byte>[] { Payload },
                PayloadEncoding = PayloadEncoding,
            };
        }
    }
}
