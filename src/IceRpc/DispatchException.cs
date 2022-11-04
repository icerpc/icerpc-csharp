// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>Represents an exception thrown by the peer while dispatching a request. It's decoded from a response with a
/// status code greater than <see cref="StatusCode.Failure" />.</summary>
public sealed class DispatchException : Exception
{
    /// <summary>Gets or sets a value indicating whether this exception should be converted to a <see
    /// cref="DispatchException" /> with the <see cref="StatusCode.UnhandledException" /> status code when
    /// thrown from a dispatcher.</summary>
    public bool ConvertToUnhandled { get; set; }

    /// <summary>Gets the status code of the corresponding response.</summary>
    public StatusCode StatusCode { get; }

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
                string message = $"{nameof(DispatchException)} {{ StatusCode = {StatusCode} }}";

                if (Origin is OutgoingRequest request)
                {
                    message += $" thrown by operation '{request.Operation}' on '{request.ServiceAddress}'";
                }

                if (InnerException is not null)
                {
                    message += $":\n{InnerException}\n---";
                }

                return message;
            }
        }
    }

    /// <summary>Gets the exception origin.</summary>
    public OutgoingRequest? Origin { get; internal init; }

    /// <summary>Gets the retry policy.</summary>
    public RetryPolicy RetryPolicy { get; }

    private readonly bool _hasCustomMessage;

    /// <summary>Constructs a new instance of <see cref="DispatchException" />.</summary>
    /// <param name="statusCode">The status code that describes the failure.</param>
    /// <param name="retryPolicy">The retry policy for the exception.</param>
    public DispatchException(StatusCode statusCode, RetryPolicy? retryPolicy = null)
        : this(message: null, statusCode, innerException: null, retryPolicy)
    {
    }

    /// <summary>Constructs a new instance of <see cref="DispatchException" />.</summary>
    /// <param name="message">Message that describes the exception.</param>
    /// <param name="statusCode">The status code that describes the failure.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    /// <param name="retryPolicy">The retry policy for the exception.</param>
    public DispatchException(
        string? message,
        StatusCode statusCode,
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
