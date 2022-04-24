// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame received by the application.</summary>
    public sealed class IncomingResponse : IncomingFrame<ResponseFieldKey>
    {
        /// <summary>The request that received this response.</summary>
        public OutgoingRequest Request { get; }

        /// <summary>Gets or initializes the <see cref="IceRpc.ResultType"/> of this response.</summary>
        /// <value>The result type of the response. The default value is <see cref="ResultType.Success"/>.</value>
        public ResultType ResultType { get; init; } = ResultType.Success;

        /// <summary>Constructs an incoming response.</summary>
        /// <param name="request">The corresponding outgoing request.</param>
        /// <param name="connection">The <see cref="Connection"/> that received the response.</param>
        /// <param name="fields">The fields of this response.</param>
        /// <param name="fieldsPipeReader">The pipe reader that holds the memory of the fields. Use <c>null</c> when the
        /// fields' memory is not held by a pipe reader.</param>
        public IncomingResponse(
            OutgoingRequest request,
            Connection connection,
            IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields,
            PipeReader? fieldsPipeReader = null)
            : base(connection, fields, fieldsPipeReader) => Request = request;

        /// <summary>Constructs an incoming response with empty fields.</summary>
        /// <param name="request">The corresponding outgoing request.</param>
        /// <param name="connection">The connection that received the response.</param>
        /// <remarks>While <paramref name="connection"/> is usually the same as the request's
        /// <see cref="OutgoingRequest.Connection"/>, it may be a different connection since an invoker can ignore the
        /// request's connection when sending this request.</remarks>
        public IncomingResponse(OutgoingRequest request, Connection connection)
            : this(request, connection, ImmutableDictionary<ResponseFieldKey, ReadOnlySequence<byte>>.Empty)
        {
        }
    }
}
