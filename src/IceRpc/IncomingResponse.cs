// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame received by the application.</summary>
    public sealed class IncomingResponse : IncomingFrame
    {
        /// <summary>Gets or initializes the fields of this response.</summary>
        public IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> Fields { get; init; } =
            ImmutableDictionary<ResponseFieldKey, ReadOnlySequence<byte>>.Empty;

        /// <summary>The request that received this response.</summary>
        public OutgoingRequest Request { get; }

        /// <summary>Gets or initializes the <see cref="IceRpc.ResultType"/> of this response.</summary>
        /// <value>The result type of the response. The default value is <see cref="ResultType.Success"/>.</value>
        public ResultType ResultType { get; init; } = ResultType.Success;

        /// <summary>Constructs an incoming response.</summary>
        /// <param name="request">The corresponding outgoing request.</param>
        /// <param name="connection">The connection that received the response.</param>
        public IncomingResponse(OutgoingRequest request, Connection connection)
            : base(connection) => Request = request;
    }
}
