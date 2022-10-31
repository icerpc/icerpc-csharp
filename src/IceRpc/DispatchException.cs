// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>Represents an exception thrown by the peer during dispatch. This exception is IDL-independent. This
/// exception is encoded in the payload of responses with the <see cref="ResultType.Failure" /> result type.
/// With icerpc, it is encoded using Slice2 as a string (the message) followed by a varuint62 (the error code).
/// With ice, it is encoded as a Slice1 system exception.</summary>
public sealed class DispatchException : Exception
{
    /// <summary>Gets or sets a value indicating whether the exception should be converted to a <see
    /// cref="DispatchException" /> with the <see cref="DispatchErrorCode.UnhandledException" /> error code when
    /// thrown from a dispatcher.</summary>
    public bool ConvertToUnhandled { get; set; }

    /// <summary>Gets or sets the error code that describes the failure.</summary>
    public DispatchErrorCode ErrorCode { get; set; }

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
                string message = $"{nameof(DispatchException)} {{ ErrorCode = {ErrorCode} }}";

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
    public OutgoingRequest? Origin { get; internal set; }

    /// <summary>Gets the retry policy.</summary>
    public RetryPolicy RetryPolicy { get; } = RetryPolicy.NoRetry;

    private readonly bool _hasCustomMessage;

    /// <summary>Constructs a new instance of <see cref="DispatchException" />.</summary>
    /// <param name="errorCode">The error code that describes the failure.</param>
    /// <param name="retryPolicy">The retry policy for the exception.</param>
    public DispatchException(DispatchErrorCode errorCode, RetryPolicy? retryPolicy = null)
    {
        ErrorCode = errorCode;
        RetryPolicy = retryPolicy ?? RetryPolicy.NoRetry;
    }

    /// <summary>Constructs a new instance of <see cref="DispatchException" />.</summary>
    /// <param name="message">Message that describes the exception.</param>
    /// <param name="errorCode">The error code that describes the failure.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    /// <param name="retryPolicy">The retry policy for the exception.</param>
    public DispatchException(
        string? message,
        DispatchErrorCode errorCode,
        Exception? innerException = null,
        RetryPolicy? retryPolicy = null)
        : base(message, innerException)
    {
        _hasCustomMessage = message is not null;
        ErrorCode = errorCode;
        RetryPolicy = retryPolicy ?? RetryPolicy.NoRetry;
    }
}
