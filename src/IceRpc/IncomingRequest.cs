// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents a request frame received by the application.</summary>
    public sealed class IncomingRequest : IncomingFrame<RequestFieldKey>
    {
        /// <summary>Gets or sets the features of this request.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

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
        /// <param name="connection">The <see cref="Connection"/> that received the request.</param>
        /// <param name="fields">The fields of this request.</param>
        /// <param name="fieldsPipeReader">The pipe reader that holds the memory of the fields. Use <c>null</c> when the
        /// fields' memory is not held by a pipe reader.</param>
        public IncomingRequest(
            Connection connection,
            IDictionary<RequestFieldKey, ReadOnlySequence<byte>> fields,
            PipeReader? fieldsPipeReader = null)
            : base(connection, fields, fieldsPipeReader)
        {
        }

        /// <summary>Constructs an incoming request with empty fields.</summary>
        /// <param name="connection">The <see cref="Connection"/> used to receive the request.</param>
        public IncomingRequest(Connection connection)
            : this(connection, ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty)
        {
        }
    }
}
