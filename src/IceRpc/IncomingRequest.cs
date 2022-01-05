// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents a request protocol frame received by the application.</summary>
    public sealed class IncomingRequest : IncomingFrame
    {
        /// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the
        /// caller is no longer interested in the response and discards the request. The server-side runtime does not
        /// enforce this deadline - it's provided "for information" to the application. The Ice client runtime sets
        /// this deadline automatically using the proxy's invocation timeout and sends it with icerpc requests but not
        /// with ice requests. As a result, the deadline for an ice request is always <see cref="DateTime.MaxValue"/>
        /// on the server-side even though the invocation timeout is usually not infinite.</summary>
        public DateTime Deadline { get; init; }

        /// <summary>The fragment of the target service.</summary>
        public string Fragment { get; init; }

        /// <summary>When <c>true</c>, the operation is idempotent.</summary>
        public bool IsIdempotent { get; init; }

        /// <summary><c>True</c> for oneway requests, <c>false</c> otherwise.</summary>
        public bool IsOneway { get; init; }

        /// <summary>The operation called on the service.</summary>
        public string Operation { get; init; }

        /// <summary>The path of the target service.</summary>
        public string Path { get; init; }

        /// <summary>Gets the cancellation dispatch source.</summary>
        internal CancellationTokenSource? CancelDispatchSource { get; set; }

        /// <summary>The pipe writer used by IceRPC to write the response, including the response header. This is also
        /// the outgoing response's payload sink a middleware would see if no middleware decorates this response's
        /// payload sink.</summary>
        internal PipeWriter ResponseWriter { get; }

        /// <summary>Constructs an incoming request.</summary>
        /// <param name="protocol">The <see cref="Protocol"/> used to send the request.</param>
        /// <param name="path">The path of the request.</param>
        /// <param name="fragment">The fragment of the request.</param>
        /// <param name="operation">The operation of the request.</param>
        /// <param name="payload">The payload of the request.</param>
        /// <param name="payloadEncoding">The encoding of the payload.</param>
        /// <param name="responseWriter">The response writer.</param>
        internal IncomingRequest(
            Protocol protocol,
            string path,
            string fragment,
            string operation,
            PipeReader payload,
            Encoding payloadEncoding,
            PipeWriter responseWriter) :
            base(protocol, payload, payloadEncoding)
        {
            Path = path;
            Fragment = fragment;
            Operation = operation;
            ResponseWriter = responseWriter;
        }
    }
}
