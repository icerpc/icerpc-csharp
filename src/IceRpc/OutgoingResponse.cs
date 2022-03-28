// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame sent by the application.</summary>
    public sealed class OutgoingResponse : OutgoingFrame
    {
        /// <summary>Gets or sets the fields of this response.</summary>
        public IDictionary<ResponseFieldKey, OutgoingFieldValue> Fields { get; set; } =
            ImmutableDictionary<ResponseFieldKey, OutgoingFieldValue>.Empty;

        /// <summary>Returns the corresponding incoming request.</summary>
        public IncomingRequest Request { get; }

        /// <summary>Gets or initializes the <see cref="IceRpc.ResultType"/> of this response.</summary>
        /// <value>The result type of this response. The default is <see cref="ResultType.Success"/>.</value>
        public ResultType ResultType { get; init; } = ResultType.Success;

        /// <summary>Constructs an outgoing response.</summary>
        /// <param name="request">The incoming request.</param>
        public OutgoingResponse(IncomingRequest request)
            : base(request.Protocol) => Request = request;
    }
}
