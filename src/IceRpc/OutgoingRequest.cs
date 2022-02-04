// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents an ice or icerpc request frame sent by the application.</summary>
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
        /// caller is no longer interested in the response and discards the request. This deadline is sent with icerpc
        /// requests but not with ice requests.</summary>
        /// <remarks>The source of the cancellation token given to an invoker alongside this outgoing request is
        /// expected to enforce this deadline.</remarks>
        public DateTime Deadline { get; set; } = DateTime.MaxValue;

        /// <summary>The features of this request.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>The fragment of the target service.</summary>
        public string Fragment { get; }

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

        /// <summary>The parameters of this request.</summary>
        public ImmutableDictionary<string, string> Params { get; set; }

        /// <summary>The path of the target service.</summary>
        public string Path { get; }

        /// <summary>The proxy that is sending this request.</summary>
        public Proxy Proxy { get; }

        /// <summary>The payload sink that an interceptor would see unless some other interceptor decorates it.
        /// </summary>
        internal DelayedPipeWriterDecorator InitialPayloadSink { get; }

        /// <summary>A pipe reader used to read the response. The protocol connection implementation may or may not set
        /// this property when sending the request.</summary>
        internal PipeReader? ResponseReader { get; set; }

        /// <summary>Constructs an outgoing request.</summary>
        /// <param name="proxy">The <see cref="Proxy"/> used to send the request.</param>
        /// <param name="operation">The operation of the request.</param>
        public OutgoingRequest(Proxy proxy, string operation) :
            base(proxy.Protocol, new DelayedPipeWriterDecorator())
        {
            AltEndpoints = proxy.AltEndpoints;
            Connection = proxy.Connection;
            Fragment = proxy.Fragment;

            // We keep it to initialize it later
            InitialPayloadSink = (DelayedPipeWriterDecorator)PayloadSink;

            Endpoint = proxy.Endpoint;
            Operation = operation;
            Params = proxy.Params;
            Path = proxy.Path;
            Proxy = proxy;
        }
    }
}
