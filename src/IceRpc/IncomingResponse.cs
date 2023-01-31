// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>Represents a response protocol frame received by the application.</summary>
public sealed class IncomingResponse : IncomingFrame
{
    /// <summary>Gets the error message of this response. The error message is null when <see cref="StatusCode" /> is
    /// <see cref="StatusCode.Success" />. Otherwise, it is non-null.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Gets the fields of this incoming response.</summary>
    public IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> Fields { get; private set; }

    /// <summary>Gets the <see cref="StatusCode" /> of this response.</summary>
    public StatusCode StatusCode { get; }

    private readonly PipeReader? _fieldsPipeReader;

    /// <summary>Constructs an incoming response with empty fields.</summary>
    /// <param name="request">The corresponding outgoing request.</param>
    /// <param name="connectionContext">The connection context of the connection that received this response.</param>
    /// <param name="statusCode">The status code of this response.</param>
    /// <param name="errorMessage">The error message of this response.</param>
    public IncomingResponse(
        OutgoingRequest request,
        IConnectionContext connectionContext,
        StatusCode statusCode = StatusCode.Success,
        string? errorMessage = null)
        : this(
            request,
            connectionContext,
            statusCode,
            errorMessage,
            ImmutableDictionary<ResponseFieldKey, ReadOnlySequence<byte>>.Empty,
            fieldsPipeReader: null)
    {
    }

    /// <summary>Constructs an incoming response.</summary>
    /// <param name="request">The corresponding outgoing request.</param>
    /// <param name="connectionContext">The connection context of the connection that received this response.</param>
    /// <param name="statusCode">The status code of this response.</param>
    /// <param name="errorMessage">The error message of this response.</param>
    /// <param name="fields">The fields of this response.</param>
    public IncomingResponse(
        OutgoingRequest request,
        IConnectionContext connectionContext,
        StatusCode statusCode,
        string? errorMessage,
        IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields)
        : this(request, connectionContext, statusCode, errorMessage, fields, fieldsPipeReader: null)
    {
    }

    /// <summary>Constructs an incoming response with a pipe reader holding the memory for the fields.</summary>
    /// <param name="request">The corresponding outgoing request.</param>
    /// <param name="connectionContext">The connection context of the connection that received this response.</param>
    /// <param name="statusCode">The status code of this response.</param>
    /// <param name="errorMessage">The error message of this response.</param>
    /// <param name="fields">The fields of this response.</param>
    /// <param name="fieldsPipeReader">The pipe reader that holds the memory of the fields. Use <c>null</c> when the
    /// fields memory is not held by a pipe reader.</param>
    internal IncomingResponse(
        OutgoingRequest request,
        IConnectionContext connectionContext,
        StatusCode statusCode,
        string? errorMessage,
        IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields,
        PipeReader? fieldsPipeReader)
        : base(request.Protocol, connectionContext)
    {
        if (statusCode == StatusCode.Success)
        {
            if (errorMessage is not null)
            {
                throw new ArgumentException(
                    $"The {nameof(errorMessage)} argument must be null when {nameof(statusCode)} is {nameof(StatusCode.Success)}.",
                    nameof(errorMessage));
            }
        }
        else if (errorMessage is null)
        {
            throw new ArgumentException(
                $"The {nameof(errorMessage)} argument must be non-null when {nameof(statusCode)} is greater than {nameof(StatusCode.Success)}.",
                nameof(errorMessage));
        }

        StatusCode = statusCode;
        ErrorMessage = errorMessage;
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
