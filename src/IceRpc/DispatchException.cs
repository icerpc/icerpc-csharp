// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>Represents an exception thrown by the peer while dispatching a request. It's decoded from a response with a
/// status code greater than <see cref="StatusCode.Failure" />.</summary>
public sealed class DispatchException : Exception
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

    /// <inheritdoc/>
    public override string Message
    {
        get
        {
            if (_hasCustomMessage)
            {
                return base.Message;
            }
            else
            {
                // We always give a custom message to dispatch exceptions we decode so this code is only used for
                // non-decoded dispatch exceptions.

                string message = $"{nameof(DispatchException)} {{ StatusCode = {StatusCode} }}";

                if (InnerException is not null)
                {
                    message += $":\n{InnerException}\n---";
                }
                return message;
            }
        }
    }

    /// <summary>Gets the retry policy.</summary>
    public RetryPolicy RetryPolicy { get; }

    /// <summary>Gets the status code.</summary>
    public StatusCode StatusCode { get; }

    private readonly bool _hasCustomMessage;

    /// <summary>Constructs a new instance of <see cref="DispatchException" />.</summary>
    /// <param name="statusCode">The status code of this exception. It must be greater than
    /// <see cref="StatusCode.Failure" />.</param>
    /// <param name="retryPolicy">The retry policy for the exception.</param>
    public DispatchException(StatusCode statusCode, RetryPolicy? retryPolicy = null)
        : this(statusCode, message: null, innerException: null, retryPolicy)
    {
    }

    /// <summary>Constructs a new instance of <see cref="DispatchException" />.</summary>
    /// <param name="message">Message that describes the exception.</param>
    /// <param name="statusCode">The status code of this exception. It must be greater than
    /// <see cref="StatusCode.Failure" />.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    /// <param name="retryPolicy">The retry policy for the exception.</param>
    public DispatchException(
        StatusCode statusCode,
        string? message,
        Exception? innerException = null,
        RetryPolicy? retryPolicy = null)
        : base(message, innerException)
    {
        _hasCustomMessage = message is not null;
        StatusCode = statusCode > StatusCode.Failure ? statusCode :
            throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                $"the status code of a {nameof(DispatchException)} must be greater than {nameof(StatusCode.Failure)}");
        RetryPolicy = retryPolicy ?? RetryPolicy.NoRetry;
    }
}
