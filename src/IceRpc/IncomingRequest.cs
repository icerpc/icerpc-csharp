// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>Represents a request frame received by the application.</summary>
    public sealed class IncomingRequest : IncomingFrame
    {
        /// <summary>Gets or sets the features of this request.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>Gets or initializes the fields of this request.</summary>
        public IDictionary<RequestFieldKey, ReadOnlySequence<byte>> Fields { get; init; } =
            ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty;

        /// <summary>Gets or initializes the fragment of the target service.</summary>
        /// <value>The fragment of the target service. The default is the empty string, and it is always the empty
        /// string with the icerpc protocol.</value>
        public string Fragment
        {
            get => _fragment;
            init => _fragment = Protocol == Protocol.Ice || value.Length == 0 ? value :
                throw new InvalidOperationException("cannot create an icerpc request with a non-empty fragment");
        }

        /// <summary>Gets or initializes whether this request is oneway or two-way.</summary>
        /// <value><c>true</c> for oneway requests, <c>false</c> otherwise. The default is <c>false</c>.</value>
        public bool IsOneway { get; init; }

        /// <summary>Gets or initializes the name of the operation to call on the target service.</summary>
        /// <value>The name of the operation. The default is the empty string.</value>
        public string Operation { get; init; } = "";

        /// <summary>Gets or initializes the path of the target service.</summary>
        /// <value>The path of the target service. The default is <c>/</c>.</value>
        public string Path { get; init; } = "/";

        private readonly string _fragment = "";

        /// <summary>Constructs an incoming request.</summary>
        /// <param name="protocol">The <see cref="Protocol"/> used to receive the request.</param>
        public IncomingRequest(Protocol protocol)
            : base(protocol)
        {
        }
    }
}
