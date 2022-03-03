// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents a request frame received by the application.</summary>
    public sealed class IncomingRequest : IncomingFrame
    {
        /// <summary>The features of this request.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>The fragment of the target service. It's always empty with the icerpc protocol.</summary>
        public string Fragment { get; }

        /// <summary><c>True</c> for oneway requests, <c>false</c> otherwise.</summary>
        public bool IsOneway { get; init; }

        /// <summary>The operation called on the service.</summary>
        public string Operation { get; }

        /// <summary>The path of the target service.</summary>
        public string Path { get; }

        /// <summary>Returns the encoding of the payload of this request.</summary>
        public Encoding PayloadEncoding { get; }

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
            PipeWriter responseWriter)
            : base(protocol, payload)
        {
            Path = path;
            Fragment = fragment;
            Operation = operation;
            PayloadEncoding = payloadEncoding;
            ResponseWriter = responseWriter;
        }

        internal override async ValueTask CompleteAsync(Exception? exception = null)
        {
            await base.CompleteAsync(exception).ConfigureAwait(false);

            await ResponseWriter.CompleteAsync(exception).ConfigureAwait(false);
        }
    }
}
