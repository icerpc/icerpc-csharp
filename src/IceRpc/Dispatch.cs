// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Holds properties that describe the request being dispatched. You can also set entries in
    /// <see cref="ResponseFeatures"/> to communicate with a middleware "on the way back".</summary>
    public sealed class Dispatch
    {
        /// <summary>The <see cref="Connection"/> over which the request was dispatched.</summary>
        public Connection Connection => IncomingRequest.Connection;

        /// <summary>Gets the value of the Context features in <see cref="RequestFeatures"/>.</summary>
        public IDictionary<string, string> Context => RequestFeatures.GetContext();

        /// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the
        /// caller is no longer interested in the response and discards the request. The server-side runtime does not
        /// enforce this deadline - it's provided "for information" to the application. The IceRPC client runtime sets
        /// this deadline automatically using the proxy's invocation timeout and sends it with icerpc requests but not
        /// with ice requests. As a result, the deadline for an ice request is always <see cref="DateTime.MaxValue"/>
        /// on the server-side even though the invocation timeout is usually not infinite.</summary>
        public DateTime Deadline => IncomingRequest.Deadline;

        /// <summary>The encoding used by the request.</summary>
        public Encoding Encoding => IncomingRequest.PayloadEncoding;

        /// <summary><c>True</c> if the operation was marked as idempotent, <c>False</c> otherwise.</summary>
        public bool IsIdempotent => IncomingRequest.IsIdempotent;

        /// <summary><c>True</c> for oneway requests, <c>False</c> otherwise.</summary>
        public bool IsOneway => IncomingRequest.IsOneway;

        /// <summary>The operation name.</summary>
        public string Operation => IncomingRequest.Operation;

        /// <summary>The path (percent-escaped).</summary>
        public string Path => IncomingRequest.Path;

        /// <summary>The protocol used by the request.</summary>
        public Protocol Protocol => IncomingRequest.Protocol;

        /// <summary>The invoker assigned to any proxy read from the payload of this request.</summary>
        public IInvoker? ProxyInvoker => IncomingRequest.ProxyInvoker;

        /// <summary>The features associated with the request.</summary>
        public FeatureCollection RequestFeatures => IncomingRequest.Features;

        /// <summary>The features associated with the response.</summary>
        public FeatureCollection ResponseFeatures { get; set; } = FeatureCollection.Empty;

        /// <summary>The incoming request frame.</summary>
        internal IncomingRequest IncomingRequest { get; }

        internal Dispatch(IncomingRequest request) => IncomingRequest = request;
    }
}
