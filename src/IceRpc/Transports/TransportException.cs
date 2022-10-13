// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>The possible error codes carried by a <see cref="TransportException" />. The error code specifies the
/// reason of the transport failure.</summary>
public enum TransportErrorCode
{
    /// <summary>The listener local address is in use.</summary>
    AddressInUse,

    /// <summary>The peer closed the connection. With multiplexed transports, <see
    /// cref="TransportException.ApplicationErrorCode" /> is set to the error code provided to <see
    /// cref="IMultiplexedConnection.CloseAsync" />.</summary>
    ConnectionClosed,

    /// <summary>The connection was disposed.</summary>
    ConnectionDisposed,

    /// <summary>The connection was idle and timed-out.</summary>
    ConnectionIdle,

    /// <summary>The peer refused the connection.</summary>
    ConnectionRefused,

    /// <summary>The connection was reset by the peer.</summary>
    ConnectionReset,

    /// <summary>The connection was shutdown.</summary>
    ConnectionShutdown,

    /// <summary>An internal error occurred.</summary>
    InternalError,

    /// <summary>An other unspecified error occurred.</summary>
    Unspecified,
}

/// <summary>This exception is thrown by transport implementation to report errors in a transport-independent manner.
/// Transport implementations should wrap transport-specific exceptions with this exception.</summary>
public class TransportException : Exception
{
    /// <summary>Gets the application protocol error code. It's set when this exception is triggered by the closure of a
    /// multiplexed connection by the remote peer. <see cref = "ErrorCode" /> is <see
    /// cref="TransportErrorCode.ConnectionClosed" /> in this situation. In all other situations, this property is null.
    /// The remote peer specifies the application error code when calling <see cref=
    /// "IMultiplexedConnection.CloseAsync" />.</summary>
    public ulong? ApplicationErrorCode { get; }

    /// <summary>Gets the transport error code.</summary>
    public TransportErrorCode ErrorCode { get; }

    /// <summary>Constructs a new instance of the <see cref="TransportException" /> class with a specified error
    /// code.</summary>
    /// <param name="errorCode">The error code.</param>
    public TransportException(TransportErrorCode errorCode)
        : base($"{nameof(TransportException)} {{ ErrorCode = {errorCode} }}") => ErrorCode = errorCode;

    /// <summary>Constructs a new instance of the <see cref="TransportException" /> class with a specified error
    /// code and message.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The message.</param>
    public TransportException(TransportErrorCode errorCode, string message)
        : base(message) => ErrorCode = errorCode;

    /// <summary>Constructs a new instance of the <see cref="TransportException" /> class with a specified error
    /// code and application error code.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="applicationErrorCode">The application error code.</param>
    public TransportException(TransportErrorCode errorCode, ulong applicationErrorCode)
        : base($"{nameof(TransportException)} {{ ErrorCode = {errorCode}, ApplicationErrorCode = {applicationErrorCode} }}")
    {
        ErrorCode = errorCode;
        ApplicationErrorCode = applicationErrorCode;
    }

    /// <summary>Constructs a new instance of the <see cref="TransportException" /> class with a specified error code
    /// and a reference to the inner exception that is the cause of this exception.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public TransportException(TransportErrorCode errorCode, Exception innerException)
        : base($"{nameof(TransportException)} {{ ErrorCode = {errorCode} }}", innerException) => ErrorCode = errorCode;

    /// <summary>Constructs a new instance of the <see cref="TransportException"/> class with a specified error code,
    /// application error code and a reference to the inner exception that is the cause of this exception.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="applicationErrorCode">The application error code.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public TransportException(TransportErrorCode errorCode, ulong applicationErrorCode, Exception innerException)
        : base(
            $"{nameof(TransportException)} {{ ErrorCode = {errorCode}, ApplicationErrorCode = {applicationErrorCode} }}",
            innerException)
    {
        ErrorCode = errorCode;
        ApplicationErrorCode = applicationErrorCode;
    }
}
