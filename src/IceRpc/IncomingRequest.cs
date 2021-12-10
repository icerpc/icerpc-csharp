// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents a request protocol frame received by the application.</summary>
    public sealed class IncomingRequest : IncomingFrame
    {
        /// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the
        /// caller is no longer interested in the response and discards the request. The server-side runtime does not
        /// enforce this deadline - it's provided "for information" to the application. The Ice client runtime sets
        /// this deadline automatically using the proxy's invocation timeout and sends it with ice2 requests but not
        /// with ice1 requests. As a result, the deadline for an ice1 request is always <see cref="DateTime.MaxValue"/>
        /// on the server-side even though the invocation timeout is usually not infinite.</summary>
        public DateTime Deadline { get; init; }

        /// <summary>When <c>true</c>, the operation is idempotent.</summary>
        public bool IsIdempotent { get; init; }

        /// <summary><c>True</c> for oneway requests, <c>false</c> otherwise.</summary>
        public bool IsOneway { get; init; }

        /// <summary>The operation called on the service.</summary>
        public string Operation { get; init; }

        /// <summary>The path of the target service.</summary>
        public string Path { get; init; }

        /// <summary>The invoker assigned to any proxy read from the payload of this request.</summary>
        public IInvoker? ProxyInvoker { get; set; }

        /// <summary>Get the cancellation dispatch source.</summary>
        internal CancellationTokenSource? CancelDispatchSource { get; set; }

        /// <summary>The initial payload sink of a response created for this request.</summary>
        internal PipeWriter InitialResponsePayloadSink { get; }

        /// <summary>The stream used to receive the request.</summary>
        internal IMultiplexedStream? Stream { get; init; }

        /// <summary>Constructs an incoming request.</summary>
        /// <param name="protocol">The <see cref="Protocol"/> used to send the request.</param>
        /// <param name="path">The path of the request.</param>
        /// <param name="operation">The operation of the request.</param>
        /// <param name="payload">The payload of the request.</param>
        /// <param name="payloadEncoding">The encoding of the payload.</param>
        /// <param name="responsePayloadSink">The initial payload sink of a response created for this request.</param>
        internal IncomingRequest(
            Protocol protocol,
            string path,
            string operation,
            PipeReader payload,
            Encoding payloadEncoding,
            PipeWriter responsePayloadSink) :
            base(protocol, payload, payloadEncoding)
        {
            Path = path;
            Operation = operation;
            InitialResponsePayloadSink = responsePayloadSink;
        }
    }
}
