// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Represents an exception thrown while dispatching a request. It's encoded as a response with a status code
/// greater than <see cref="StatusCode.Ok" />.</summary>
public sealed class DispatchException : Exception
{
    /// <summary>Gets or sets a value indicating whether the exception should be converted into a <see
    /// cref="DispatchException" /> with status code <see cref="StatusCode.InternalError" /> when thrown from a
    /// dispatch.</summary>
    /// <value>When <see langword="true" />, this exception is converted into dispatch exception with status code <see
    /// cref="StatusCode.InternalError" /> just before it's encoded. Defaults to <see langword="true" /> for an
    /// exception decoded from an <see cref="IncomingResponse" />, and <see langword="false" /> for an exception created
    /// by the application using a constructor of <see cref="DispatchException" />.</value>
    public bool ConvertToInternalError { get; set; }

    /// <summary>Gets the status code.</summary>
    /// <value>The <see cref="IceRpc.StatusCode" /> of this exception.</value>
    public StatusCode StatusCode { get; }

    /// <summary>Constructs a new instance of <see cref="DispatchException" />.</summary>
    /// <param name="statusCode">The status code of this exception. It must be greater than <see
    /// cref="StatusCode.Ok" />.</param>
    /// <param name="message">A message that describes the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="statusCode" /> is equal to <see
    /// cref="StatusCode.Ok" />.</exception>
    public DispatchException(
        StatusCode statusCode,
        string? message = null,
        Exception? innerException = null)
        : base(message ?? GetDefaultMessage(statusCode), innerException) =>
        StatusCode = statusCode > StatusCode.Ok ? statusCode :
            throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                $"The status code of a {nameof(DispatchException)} must be greater than {nameof(StatusCode.Ok)}.");

    /// <summary>Construct an outgoing response from this dispatch exception.</summary>
    /// <param name="request">The incoming request.</param>
    /// <returns>The outgoing response.</returns>
    public OutgoingResponse ToOutgoingResponse(IncomingRequest request)
    {
        if (ConvertToInternalError)
        {
            return new OutgoingResponse(request, StatusCode.InternalError, message: null, this);
        }
        else
        {
            return new OutgoingResponse(request, StatusCode, Message, InnerException);
        }
    }

    private static string? GetDefaultMessage(StatusCode statusCode) =>
        statusCode == StatusCode.ApplicationError ? null : $"The dispatch failed with status code {statusCode}.";
}
