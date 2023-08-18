// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace IceRpc;

/// <summary>Represents a response frame sent by the application.</summary>
public sealed class OutgoingResponse : OutgoingFrame
{
    /// <summary>Gets the error message of this response.</summary>
    /// <value>The error message of this response if <see cref="StatusCode" /> is different from <see
    /// cref="StatusCode.Ok" />; otherwise, <see langword="null" />.</value>
    public string? ErrorMessage { get; }

    /// <summary>Gets or sets the fields of this response.</summary>
    /// <value>The fields of this incoming response. Defaults to <see cref="ImmutableDictionary{TKey, TValue}.Empty"
    /// />.</value>
    public IDictionary<ResponseFieldKey, OutgoingFieldValue> Fields { get; set; } =
        ImmutableDictionary<ResponseFieldKey, OutgoingFieldValue>.Empty;

    /// <summary>Gets the status code of this response.</summary>
    /// <value>The <see cref="IceRpc.StatusCode" /> of this response.</value>
    public StatusCode StatusCode { get; }

    /// <summary>Constructs an outgoing response with the <see cref="StatusCode.Ok" /> status code and a <see
    /// langword="null" /> error message.</summary>
    /// <param name="request">The incoming request.</param>
    /// <remarks>The constructor also associates this response with the request. If another response is already set on
    /// the request, its payload and payload continuation are completed.</remarks>
    public OutgoingResponse(IncomingRequest request)
        : base(request.Protocol)
    {
        request.Response = this;
        StatusCode = StatusCode.Ok;
    }

    /// <summary>Constructs an outgoing response.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="statusCode">The status code. It must be greater than <see cref="StatusCode.Ok" />.</param>
    /// <param name="message">The error message or null to use the default error message.</param>
    /// <param name="exception">The exception that is the cause of this failure.</param>
    /// <remarks>The constructor also associates this response with the request. If another response is already set on
    /// the request, its payload and payload continuation are completed.</remarks>
    public OutgoingResponse(
        IncomingRequest request,
        StatusCode statusCode,
        string? message = null,
        Exception? exception = null)
        : base(request.Protocol)
    {
        request.Response = this;
        StatusCode = statusCode > StatusCode.Ok ? statusCode :
            throw new ArgumentException(
                $"The status code for an exception must be greater than {nameof(StatusCode.Ok)}.",
                nameof(statusCode));

        string errorMessage = message ?? $"The dispatch failed with status code {statusCode}.";
        if (exception is not null)
        {
            errorMessage += $" The failure was caused by an exception of type '{exception.GetType()}' with message: {exception.Message}";
        }
        ErrorMessage = errorMessage;
    }
}
