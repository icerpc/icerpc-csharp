// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
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

        internal RetryPolicy GetRetryPolicy(Proxy proxy)
        {
            RetryPolicy retryPolicy = RetryPolicy.NoRetry;
            if (PayloadEncoding == Encoding.Ice11)
            {
                // For compatibility with ZeroC Ice
                if (ReplyStatus == ReplyStatus.ObjectNotExistException &&
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
