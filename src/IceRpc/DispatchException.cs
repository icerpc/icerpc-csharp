// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Represents an exception thrown while dispatching a request. It's encoded as a response with a status code
/// greater than <see cref="StatusCode.Success" />.</summary>
public sealed class DispatchException : Exception
{
    /// <summary>Gets or sets a value indicating whether the exception should be converted into a <see
    /// cref="DispatchException" /> with status code <see cref="StatusCode.UnhandledException" /> when thrown from a
    /// dispatch.</summary>
    /// <value>When <see langword="true" />, this exception is converted into dispatch exception with status code <see
    /// cref="StatusCode.UnhandledException" /> just before it's encoded. Defaults to <see langword="true" /> for an
    /// exception decoded from an <see cref="IncomingResponse" />, and <see langword="false" /> for an exception created
    /// by the application using a constructor of <see cref="DispatchException" />.</value>
    public bool ConvertToUnhandled { get; set; }

    /// <summary>Gets the status code.</summary>
    /// <value>The <see cref="IceRpc.StatusCode" /> of this exception.</value>
    public StatusCode StatusCode { get; }

    /// <summary>Constructs a new instance of <see cref="DispatchException" />.</summary>
    /// <param name="statusCode">The status code of this exception. It must be greater than <see
    /// cref="StatusCode.Success" />.</param>
    /// <param name="message">A message that describes the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="statusCode" /> is equal to <see
    /// cref="StatusCode.Success" />.</exception>
    public DispatchException(
        StatusCode statusCode,
        string? message = null,
        Exception? innerException = null)
        : base(message ?? GetDefaultMessage(statusCode), innerException) =>
        StatusCode = statusCode > StatusCode.Success ? statusCode :
            throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                $"The status code of a {nameof(DispatchException)} must be greater than {nameof(StatusCode.Success)}.");

    private static string? GetDefaultMessage(StatusCode statusCode) =>
        statusCode == StatusCode.ApplicationError ? null : $"The dispatch failed with status code {statusCode}.";
}
