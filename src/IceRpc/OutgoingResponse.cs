// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace IceRpc;

/// <summary>Represents a response protocol frame sent by the application.</summary>
public sealed class OutgoingResponse : OutgoingFrame
{
    /// <summary>Gets the error message of this response.</summary>
    /// <value>The error message of this response if <see cref="StatusCode" /> is different from <see
    /// cref="StatusCode.Success" />; <see langword="null" /> otherwise.</value>
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
    public OutgoingResponse(IncomingRequest request)
        : base(request.Protocol)
    {
        request.Response = this;
        StatusCode = StatusCode.Success;
    }

    /// <summary>Constructs an outgoing response.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="statusCode">The status code. It must be greater than <see cref="StatusCode.Success" />.</param>
    /// <param name="errorMessage">The error message.</param>
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

    /// <summary>Constructs an outgoing response for a dispatch exception.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="dispatchException">The dispatchException.</param>
    public OutgoingResponse(IncomingRequest request, DispatchException dispatchException)
        : this(request, dispatchException.StatusCode, GetErrorMessage(dispatchException))
    {
    }

    // The error message includes the inner exception type and message because we don't transmit this inner exception
    // with the response.
    private static string GetErrorMessage(DispatchException exception) =>
        exception.InnerException is Exception innerException ?
            $"{exception.Message} This exception was caused by an exception of type '{innerException.GetType()}' with message: {innerException.Message}" :
            exception.Message;
}
