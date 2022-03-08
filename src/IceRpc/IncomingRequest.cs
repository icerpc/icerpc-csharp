// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents a request frame received by the application.</summary>
    public sealed class IncomingRequest : IncomingFrame
    {
        /// <summary>Gets or sets the features of this request.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>Gets or initializes the fragment of the target service. It's always empty with the icerpc protocol.
        /// </summary>
        public string Fragment
        {
            get => _fragment;
            init => _fragment = Protocol == Protocol.Ice || value.Length == 0 ? value :
                throw new InvalidOperationException("cannot create an icerpc request with a non-empty fragment");
        }

        /// <summary>Gets or initializes whether this request is oneway or two-way.</summary>
        /// <value><c>true</c> for oneway requests, <c>false</c> otherwise.</value>
        public bool IsOneway { get; init; }

        /// <summary>Gets or initializes the name of the operation to call on the target service.</summary>
        public string Operation { get; init; } = "";

        /// <summary>Gets or initializes the path of the target service.</summary>
        public string Path { get; init; } = "/";

        /// <summary>Gets or initializes the encoding of the payload of this request.</summary>
        public Encoding PayloadEncoding { get; init; } = Encoding.Unknown;

        /// <summary>Gets or sets the cancellation dispatch source.</summary>
        internal CancellationTokenSource? CancelDispatchSource { get; set; }

        /// <summary>Gets or initializes the pipe writer used by IceRPC to write the response, including the response
        /// header. This is also the outgoing response's payload sink a middleware would see if no middleware decorates
        /// this response's payload sink.</summary>
        internal PipeWriter ResponseWriter { get; init; } = InvalidPipeWriter.Instance;

        private readonly string _fragment = "";

        /// <summary>Constructs an incoming request.</summary>
        /// <param name="protocol">The <see cref="Protocol"/> used to send the request.</param>
        public IncomingRequest(Protocol protocol)
            : base(protocol)
        {
        }
    }
}
