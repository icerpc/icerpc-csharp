// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Transports;
using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>Represents an ice1 or ice2 request frame sent by the application.</summary>
    public sealed class OutgoingRequest : OutgoingFrame
    {
        /// <summary>The alternatives to <see cref="Endpoint"/>. It should be empty when Endpoint is null.</summary>
        public IEnumerable<Endpoint> AltEndpoints { get; set; } = ImmutableList<Endpoint>.Empty;

        /// <summary>The main target endpoint for this request.</summary>
        public Endpoint? Endpoint { get; set; }

        /// <summary>A list of endpoints this request does not want to establish a connection to, typically because a
        /// previous attempt asked the request not to.</summary>
        public IEnumerable<Endpoint> ExcludedEndpoints { get; set; } = ImmutableList<Endpoint>.Empty;

        /// <summary>The connection that will be used (or was used ) to send this request.</summary>
        public Connection? Connection { get; set; }

        /// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the
        /// caller is no longer interested in the response and discards the request. This deadline is sent with ice2
        /// requests but not with ice1 requests.</summary>
        /// <remarks>The source of the cancellation token given to an invoker alongside this outgoing request is
        /// expected to enforce this deadline.</remarks>
        public DateTime Deadline { get; set; } = DateTime.MaxValue;

        /// <summary>When true, the operation is idempotent.</summary>
        public bool IsIdempotent { get; init; }

        /// <summary>When true and the operation returns void, the request is sent as a oneway request. Otherwise, the
        /// request is sent as a twoway request.</summary>
        public bool IsOneway { get; init; }

        /// <summary>Indicates whether or not this request has been sent.</summary>
        /// <value>When <c>true</c>, the request was sent. When <c>false</c> the request was not sent yet.</value>
        public bool IsSent { get; set; }

        /// <summary>The operation called on the service.</summary>
        public string Operation { get; }

        /// <summary>The path of the target service.</summary>
        public string Path { get; }

        /// <summary>The proxy that is sending this request.</summary>
        public Proxy? Proxy { get; init; }

        /// <summary>A stream parameter decompressor. Middleware or interceptors can use this property to
        /// decompress a stream return value.</summary>
        public Func<CompressionFormat, System.IO.Stream, System.IO.Stream>? StreamDecompressor { get; set; }

        /// <summary>The retry policy for the request. The policy is used by the retry invoker if the request fails
        /// with a local exception. It is set by the connection code based on the context of the failure.</summary>
        internal RetryPolicy RetryPolicy { get; set; } = RetryPolicy.NoRetry;

        /// <summary>The stream used to send the request.</summary>
        internal INetworkStream? Stream { get; set; }

        /// <summary>Constructs an outgoing request.</summary>
        /// <param name="protocol">The <see cref="Protocol"/> used to send the request.</param>
        /// <param name="path">The path of the request.</param>
        /// <param name="operation">The operation of the request.</param>
        public OutgoingRequest(Protocol protocol, string path, string operation) :
            base(protocol)
        {
            Path = path;
            Operation = operation;
        }
    }
}
