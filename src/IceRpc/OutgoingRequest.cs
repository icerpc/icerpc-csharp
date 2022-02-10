// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents an ice or icerpc request frame sent by the application.</summary>
    public sealed class OutgoingRequest : OutgoingFrame
    {
        /// <summary>The connection that will be used (or was used ) to send this request.</summary>
        public Connection? Connection { get; set; }

        /// <summary>The features of this request.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>The fragment of the target service.</summary>
        public string Fragment { get; }

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
            Connection = proxy.Connection;
            Fragment = proxy.Fragment;

            // We keep it to initialize it later
            InitialPayloadSink = (DelayedPipeWriterDecorator)PayloadSink;
            Operation = operation;
            Params = proxy.Params;
            Path = proxy.Path;
            Proxy = proxy;
        }
    }
}
