// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame received by the application.</summary>
    public sealed class IncomingResponse : IncomingFrame
    {
        /// <summary>Gets the fields of this incoming response.</summary>
        public IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> Fields { get; private set; }

        /// <summary>Gets or initializes the <see cref="IceRpc.ResultType"/> of this response.</summary>
        /// <value>The result type of the response. The default value is <see cref="ResultType.Success"/>.</value>
        public ResultType ResultType { get; init; } = ResultType.Success;

        private readonly PipeReader? _fieldsPipeReader;

        /// <summary>Constructs an incoming response with empty fields.</summary>
        /// <param name="request">The corresponding outgoing request.</param>
        /// <param name="connection">The connection that received the response.</param>
        /// <remarks>While <paramref name="connection"/> is usually the same as the request's
        /// <see cref="OutgoingRequest.Connection"/>, it may be a different connection since an invoker can ignore the
        /// request's connection when sending the request.</remarks>
        public IncomingResponse(OutgoingRequest request, Connection connection)
            : this(request, connection, ImmutableDictionary<ResponseFieldKey, ReadOnlySequence<byte>>.Empty, null)
        {
        }

        /// <summary>Constructs an incoming response.</summary>
        /// <param name="request">The corresponding outgoing request.</param>
        /// <param name="connection">The connection that received the response.</param>
        /// <param name="fields">The fields of this response.</param>
        /// <remarks>While <paramref name="connection"/> is usually the same as the request's
        /// <see cref="OutgoingRequest.Connection"/>, it may be a different connection since an invoker can ignore the
        /// request's connection when sending the request.</remarks>
        public IncomingResponse(
            OutgoingRequest request,
            Connection connection,
            IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields)
            : this(request, connection, fields, null)
        {
        }

        /// <summary>Completes the payload and releases the fields memory.</summary>
        public void Complete(Exception? exception = null)
        {
            Payload.Complete(exception);
            _fieldsPipeReader?.Complete(exception);
            Fields = ImmutableDictionary<ResponseFieldKey, ReadOnlySequence<byte>>.Empty;
        }

        /// <summary>Constructs an incoming response with a pipe reader holding the memory for the fields.</summary>
        /// <param name="request">The corresponding outgoing request.</param>
        /// <param name="connection">The <see cref="Connection"/> that received the response.</param>
        /// <param name="fields">The fields of this response.</param>
        /// <param name="fieldsPipeReader">The pipe reader that holds the memory of the fields. Use <c>null</c> when the
        /// fields memory is not held by a pipe reader.</param>
        internal IncomingResponse(
            OutgoingRequest request,
            Connection connection,
            IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields,
            PipeReader? fieldsPipeReader)
            : base(connection)
        {
            Fields = fields;
            _fieldsPipeReader = fieldsPipeReader;
            request.Response = this;
        }
    }
}
