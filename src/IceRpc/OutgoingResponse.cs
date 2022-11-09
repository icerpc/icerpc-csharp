// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc;

/// <summary>Represents a response protocol frame sent by the application.</summary>
public sealed class OutgoingResponse : OutgoingFrame
{
    /// <summary>Gets or sets the fields of this response.</summary>
    public IDictionary<ResponseFieldKey, OutgoingFieldValue> Fields { get; set; } =
        ImmutableDictionary<ResponseFieldKey, OutgoingFieldValue>.Empty;

    /// <summary>Gets or initializes the <see cref="StatusCode" /> of this response.</summary>
    /// <value>The status code of this response. The default is <see cref="StatusCode.Success" />.</value>
    public StatusCode StatusCode { get; init; } = StatusCode.Success;

    /// <summary>Constructs an outgoing response.</summary>
    /// <param name="request">The incoming request.</param>
    public OutgoingResponse(IncomingRequest request)
        : base(request.Protocol) => request.Response = this;
}
