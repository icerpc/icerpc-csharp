// Copyright (c) ZeroC, Inc. All rights reserved.

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

    /// <summary>Gets the retry policy.</summary>
    public RetryPolicy RetryPolicy { get; }

    /// <summary>Gets the status code.</summary>
    public StatusCode StatusCode { get; }

    /// <summary>Constructs a new instance of <see cref="DispatchException" />.</summary>
    /// <param name="statusCode">The status code of this exception. It must be greater than
    /// <see cref="StatusCode.Success" />.</param>
    /// <param name="retryPolicy">The retry policy for the exception.</param>
    public DispatchException(StatusCode statusCode, RetryPolicy? retryPolicy = null)
        : this(statusCode, message: GetDefaultMessage(statusCode), innerException: null, retryPolicy)
    {
    }

    /// <summary>Constructs a new instance of <see cref="DispatchException" />.</summary>
    /// <param name="statusCode">The status code of this exception. It must be greater than
    /// <see cref="StatusCode.Success" />.</param>
    /// <param name="message">Message that describes the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    /// <param name="retryPolicy">The retry policy for the exception.</param>
    public DispatchException(
        StatusCode statusCode,
        string? message,
        Exception? innerException = null,
        RetryPolicy? retryPolicy = null)
        : base(message ?? GetDefaultMessage(statusCode), innerException)
    {
        StatusCode = statusCode > StatusCode.Success ? statusCode :
            throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                $"the status code of a {nameof(DispatchException)} must be greater than {nameof(StatusCode.Success)}");
        RetryPolicy = retryPolicy ?? RetryPolicy.NoRetry;
    }

    private static string? GetDefaultMessage(StatusCode statusCode) =>
        statusCode == StatusCode.ApplicationError ? null :
            $"{nameof(DispatchException)} {{ StatusCode = {statusCode} }}";
}
