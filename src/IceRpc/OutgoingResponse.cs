// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace IceRpc;

/// <summary>Represents a response frame sent by the application.</summary>
public sealed class OutgoingResponse : OutgoingFrame
{
    /// <summary>Gets the error message of this response.</summary>
    /// <value>The error message of this response if <see cref="StatusCode" /> is different from <see
    /// cref="StatusCode.Success" />; otherwise, <see langword="null" />.</value>
    public string? ErrorMessage { get; }

    /// <summary>Gets or sets the fields of this response.</summary>
    /// <value>The fields of this incoming response. Defaults to <see cref="ImmutableDictionary{TKey, TValue}.Empty"
    /// />.</value>
    public IDictionary<ResponseFieldKey, OutgoingFieldValue> Fields { get; set; } =
        ImmutableDictionary<ResponseFieldKey, OutgoingFieldValue>.Empty;

    /// <summary>Gets the status code of this response.</summary>
    /// <value>The <see cref="IceRpc.StatusCode" /> of this response.</value>
    public StatusCode StatusCode { get; }

    /// <summary>Constructs an outgoing response with the <see cref="StatusCode.Success" /> status code and a <see
    /// langword="null" /> error message.</summary>
    /// <param name="request">The incoming request.</param>
    /// <remarks>The constructor also associates this response with the request. If another response is already set on
    /// the request, its payload and payload continuation are completed.</remarks>
    public OutgoingResponse(IncomingRequest request)
        : base(request.Protocol)
    {
        request.Response = this;
        StatusCode = StatusCode.Success;
    }

    /// <summary>Constructs an outgoing response.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="statusCode">The status code. It must be greater than <see cref="StatusCode.Success" />.</param>
    /// <remarks>The constructor also associates this response with the request. If another response is already set on
    /// the request, its payload and payload continuation are completed.</remarks>
    public OutgoingResponse(IncomingRequest request, StatusCode statusCode)
        : this(request, statusCode, $"The dispatch failed with status code {statusCode}.")
    {
    }

    /// <summary>Constructs an outgoing response.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="statusCode">The status code. It must be greater than <see cref="StatusCode.Success" />.</param>
    /// <param name="errorMessage">The error message or null to use the default error message.</param>
    /// <remarks>The constructor also associates this response with the request. If another response is already set on
    /// the request, its payload and payload continuation are completed.</remarks>
    public OutgoingResponse(IncomingRequest request, StatusCode statusCode, string errorMessage)
        : base(request.Protocol)
    {
        request.Response = this;
        StatusCode = statusCode > StatusCode.Success ? statusCode :
            throw new ArgumentException(
                $"The status code for an exception must be greater than {nameof(StatusCode.Success)}.",
                nameof(statusCode));
        ErrorMessage = errorMessage;
    }

    /// <summary>Constructs an outgoing response for an unhandled exception.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="statusCode">The status code. It must be greater than <see cref="StatusCode.Success" />.</param>
    /// <param name="exception">The unhandled exception.</param>
    /// <remarks>The constructor also associates this response with the request. If another response is already set on
    /// the request, its payload and payload continuation are completed.</remarks>
    public OutgoingResponse(IncomingRequest request, StatusCode statusCode, Exception exception)
        : this(request, statusCode, GetErrorMessage(statusCode, exception))
    {
    }

    // The error message includes the inner exception type and message because we don't transmit this inner exception
    // with the response.
    private static string GetErrorMessage(StatusCode statusCode, Exception exception) =>
        $"The dispatch failed with status code {statusCode}. The failure was caused by an exception of type '{exception.GetType()}' with message: {exception.Message}";
}
