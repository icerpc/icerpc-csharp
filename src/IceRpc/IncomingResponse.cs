// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame received by the application.</summary>
    public sealed class IncomingResponse : IncomingFrame
    {
        /// <summary>The <see cref="IceRpc.ReplyStatus"/> of this response.</summary>
        /// <value><see cref="ReplyStatus.OK"/> when <see cref="ResultType"/> is <see
        /// cref="ResultType.Success"/>; otherwise, if <see cref="IncomingFrame.PayloadEncoding"/> is 1.1, the
        /// value is read from the response header or payload. For any other payload encoding, the value is
        /// <see cref="ReplyStatus.UserException"/>.</value>
        public ReplyStatus ReplyStatus { get; init; }

        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; init; }

        /// <summary>Create an outgoing response from this incoming response. The response is constructed to be
        /// forwarded using the given target protocol.</summary>
        /// <param name="targetProtocol">The protocol used to send to the outgoing response.</param>
        /// <returns>The outgoing response to be forwarded.</returns>
        public OutgoingResponse ToOutgoingResponse(Protocol targetProtocol)
        {
            ReadOnlyMemory<ReadOnlyMemory<byte>> payload;
            IReadOnlyDictionary<int, ReadOnlyMemory<byte>>? fields = null;
            if (targetProtocol == Protocol)
            {
                payload = new ReadOnlyMemory<byte>[] { Payload };
                if (Protocol == Protocol.Ice2)
                {
                    // Don't forward RetryPolicy
                    fields = Fields.ToImmutableDictionary().Remove((int)Ice2FieldKey.RetryPolicy);
                }
            }
            else
            {
                if (ResultType == ResultType.Failure && PayloadEncoding == Encoding.Ice11)
                {
                    // When the response carries a failure encoded with 1.1, we need to perform a small adjustment
                    // between ice1 and ice2 response frames.
                    // ice1: [failure reply status][payload size, encoding and bytes|special exception]
                    // ice2: [failure result type][payload encoding][payload size][reply status][payload bytes|
                    //                                                                          special exception bytes]
                    // There is no such adjustment with other encoding, or when the response does not carry a failure.

                    if (targetProtocol == Protocol.Ice1)
                    {
                        Debug.Assert(Protocol == Protocol.Ice2);

                        // We slice-off the reply status that is part of the ice2 payload.
                        payload = new ReadOnlyMemory<byte>[] { Payload[1..] };
                    }
                    else
                    {
                        Debug.Assert(targetProtocol == Protocol.Ice2);
                        Debug.Assert(Protocol == Protocol.Ice1);
                        // Prepend a little buffer in front of the ice2 response payload to hold the reply status
                        // TODO: we don't want little buffers!
                        payload = new ReadOnlyMemory<byte>[] { new byte[1], Payload };
                    }
                }
                else
                {
                    payload = new ReadOnlyMemory<byte>[] { Payload };
                }
            }

            return new OutgoingResponse
            {
                ResultType = ResultType,
                ReplyStatus = ReplyStatus,
                FieldsDefaults = fields ?? ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty,
                Payload = payload, // TODO: temporary, should use GetPayloadAsync()
                PayloadEncoding = PayloadEncoding,
            };
        }

        internal RetryPolicy GetRetryPolicy(Proxy? proxy)
        {
            RetryPolicy retryPolicy = RetryPolicy.NoRetry;
            if (PayloadEncoding == Encoding.Ice11)
            {
                // For compatibility with ZeroC Ice
                if (proxy != null &&
                    ReplyStatus == ReplyStatus.ObjectNotExistException &&
                    proxy.Protocol == Protocol.Ice1 &&
                    (proxy.Endpoint == null || proxy.Endpoint.Transport == TransportNames.Loc)) // "indirect" proxy
                {
                    retryPolicy = RetryPolicy.OtherReplica;
                }
            }
            else if (Fields.TryGetValue((int)Ice2FieldKey.RetryPolicy, out ReadOnlyMemory<byte> value))
            {
                retryPolicy = value.DecodeFieldValue(decoder => new RetryPolicy(decoder));
            }
            return retryPolicy;
        }
    }
}
