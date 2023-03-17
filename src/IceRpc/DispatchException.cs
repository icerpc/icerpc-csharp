// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Represents an exception thrown by the peer while dispatching a request. It's decoded from a response with a
/// status code greater than <see cref="StatusCode.Success" />.</summary>
public class DispatchException : Exception
{
    /// <summary>Gets or sets a value indicating whether the exception should be converted into a <see
    /// cref="DispatchException" /> with status code <see cref="StatusCode.UnhandledException" /> when thrown from a
    /// dispatch.</summary>
    /// <value>When <see langword="true" />, this exception is converted into dispatch exception with status code
    /// <see cref="StatusCode.UnhandledException" /> just before it's encoded. The default value is
    /// <see langword="true" /> for an exception decoded from <see cref="IncomingResponse" />, and
    /// <see langword="false" /> for an exception created by the application using a constructor of
    /// <see cref="DispatchException" />.</value>
    public bool ConvertToUnhandled { get; set; }

    /// <summary>Gets the status code.</summary>
    /// <value>The status code from <see cref="DispatchException" />.</value>
    public StatusCode StatusCode { get; }

    /// <summary>Constructs a new instance of <see cref="DispatchException" />.</summary>
    /// <param name="statusCode">The status code of this exception. It must be greater than
    /// <see cref="StatusCode.Success" />.</param>
    /// <param name="message">A message that describes the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
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
