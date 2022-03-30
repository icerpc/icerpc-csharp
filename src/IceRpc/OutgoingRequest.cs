// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents an ice or icerpc request frame sent by the application.</summary>
    public sealed class OutgoingRequest : OutgoingFrame
    {
        /// <summary>Gets or sets the connection that will be used (or was used ) to send this request.</summary>
        public Connection? Connection { get; set; }

        /// <summary>Gets or sets the features of this request.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>Gets or sets the fields of this request.</summary>
        public IDictionary<RequestFieldKey, OutgoingFieldValue> Fields { get; set; } =
            ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty;

        /// <summary>Gets or initializes whether this request is oneway or two-way.</summary>
        /// <value><c>true</c> for oneway requests, <c>false</c> otherwise. The default is <c>false</c>.</value>
        public bool IsOneway { get; init; }

        /// <summary>Indicates whether or not this request has been sent.</summary>
        /// <value>When <c>true</c>, the request was sent. When <c>false</c> the request was not sent yet.</value>
        public bool IsSent { get; set; }

        /// <summary>Gets or initializes the name of the operation to call on the target service.</summary>
        /// <value>The name of the operation. The default is the empty string.</value>
        public string Operation { get; init; } = "";

        /// <summary>Returns the proxy that is sending this request.</summary>
        public Proxy Proxy { get; }

        /// <summary>Constructs an outgoing request.</summary>
        /// <param name="proxy">The <see cref="Proxy"/> used to send the request.</param>
        public OutgoingRequest(Proxy proxy)
            : base(proxy.Protocol)
        {
            Connection = proxy.Connection;
            Proxy = proxy;
        }
    }
}
