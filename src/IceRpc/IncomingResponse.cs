// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>Represents a response protocol frame received by the application.</summary>
public sealed class IncomingResponse : IncomingFrame
{
    /// <summary>Gets the error message of this response.</summary>
    /// <value>The error message of this response if <see cref="StatusCode" /> is different from <see
    /// cref="StatusCode.Success" />; <see langword="null"/> otherwise.</value>
    public string? ErrorMessage { get; }

    /// <summary>Gets the fields of this incoming response.</summary>
    /// <value>The fields of this incoming response. Defaults to <see cref="ImmutableDictionary{TKey, TValue}.Empty"
    /// />.</value>
    public IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> Fields { get; private set; }

    /// <summary>Gets the <see cref="StatusCode" /> of this response.</summary>
    /// <value>The <see cref="IceRpc.StatusCode" /> of this response.</value>
    public StatusCode StatusCode { get; }

    private readonly PipeReader? _fieldsPipeReader;

    /// <summary>Constructs an incoming response with empty fields.</summary>
    /// <param name="request">The corresponding outgoing request.</param>
    /// <param name="connectionContext">The connection context of the connection that received this response.</param>
    /// <param name="statusCode">The status code of this response.</param>
    /// <param name="errorMessage">The error message of this response.</param>
    /// <remarks>The constructor also associates this response with the request. If another response is already set on
    /// the request, its payload and payload continuation are completed.</remarks>
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
    /// <remarks>The constructor also associates this response with the request. If another response is already set on
    /// the request, its payload and payload continuation are completed.</remarks>
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
    /// <param name="fieldsPipeReader">The pipe reader that holds the memory of the fields. Use <see langword="null"/>
    /// when the fields memory is not held by a pipe reader.</param>
    /// <remarks>The constructor also associates this response with the request. If another response is already set on
    /// the request, its payload and payload continuation are completed.</remarks>
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
