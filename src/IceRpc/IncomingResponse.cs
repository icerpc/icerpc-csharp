// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>Represents a response protocol frame received by the application.</summary>
public sealed class IncomingResponse : IncomingFrame
{
    /// <summary>Gets the fields of this incoming response.</summary>
    public IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> Fields { get; private set; }

    /// <summary>Gets or initializes the <see cref="StatusCode" /> of this response.</summary>
    /// <value>The status code of the response. The default value is <see cref="StatusCode.Success" />.</value>
    public StatusCode StatusCode { get; init; } = StatusCode.Success;

    private readonly PipeReader? _fieldsPipeReader;

    /// <summary>Constructs an incoming response with empty fields.</summary>
    /// <param name="request">The corresponding outgoing request.</param>
    /// <param name="connectionContext">The connection context of the connection that received this response.</param>
    public IncomingResponse(OutgoingRequest request, IConnectionContext connectionContext)
        : this(
            request,
            connectionContext,
            ImmutableDictionary<ResponseFieldKey, ReadOnlySequence<byte>>.Empty,
            fieldsPipeReader: null)
    {
    }

    /// <summary>Constructs an incoming response.</summary>
    /// <param name="request">The corresponding outgoing request.</param>
    /// <param name="connectionContext">The connection context of the connection that received this response.</param>
    /// <param name="fields">The fields of this response.</param>
    public IncomingResponse(
        OutgoingRequest request,
        IConnectionContext connectionContext,
        IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields)
        : this(request, connectionContext, fields, fieldsPipeReader: null)
    {
    }

    /// <summary>Constructs an incoming response with a pipe reader holding the memory for the fields.</summary>
    /// <param name="request">The corresponding outgoing request.</param>
    /// <param name="connectionContext">The connection context of the connection that received this response.</param>
    /// <param name="fields">The fields of this response.</param>
    /// <param name="fieldsPipeReader">The pipe reader that holds the memory of the fields. Use <c>null</c> when the
    /// fields memory is not held by a pipe reader.</param>
    internal IncomingResponse(
        OutgoingRequest request,
        IConnectionContext connectionContext,
        IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields,
        PipeReader? fieldsPipeReader)
        : base(connectionContext)
    {
        if (request.Protocol != connectionContext.ServerAddress.Protocol)
        {
            throw new ArgumentException(
                "the protocol of the request does not match the protocol of the connection context",
                nameof(request));
        }

        Fields = fields;
        _fieldsPipeReader = fieldsPipeReader;
        request.Response = this;
    }

    /// <summary>Completes the payload and releases the fields memory.</summary>
    /// <remarks>Dispose is internal because application code (including the Slice engine) must dispose the
    /// outgoing request that owns this incoming response or create a different incoming response that disposes the
    /// previous response held by this outgoing request.</remarks>
    internal void Dispose()
    {
        Payload.Complete();
        _fieldsPipeReader?.Complete();
        Fields = ImmutableDictionary<ResponseFieldKey, ReadOnlySequence<byte>>.Empty;
    }
}
