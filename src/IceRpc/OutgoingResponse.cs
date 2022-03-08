// Copyright (c) ZeroC, Inc. All rights reserved.

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

        /// <inheritdoc/>
        public override PipeWriter PayloadSink { get; set; }

        /// <summary>The corresponding request.</summary>
        public IncomingRequest Request { get; }

        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; init; } = ResultType.Success;

        /// <summary>Constructs an outgoing response.</summary>
        /// <param name="request">The incoming request.</param>
        public OutgoingResponse(IncomingRequest request)
            : base(request.Protocol)
        {
            Request = request;
            PayloadSink = request.ResponseWriter;
        }

        internal override async ValueTask CompleteAsync(Exception? exception = null)
        {
            await base.CompleteAsync(exception).ConfigureAwait(false);
            await PayloadSink.CompleteAsync(exception).ConfigureAwait(false);
        }
    }
}
