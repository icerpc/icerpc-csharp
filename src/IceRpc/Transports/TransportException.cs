// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>The possible error codes carried by a <see cref="IceRpcException" />. The error code specifies the
/// reason of the transport failure.</summary>
public enum IceRpcError
{
    /// <summary>The listener local address is in use.</summary>
    AddressInUse,

    /// <summary>The connection was aborted, typically by the peer. The abort can also be caused by a network failure,
    /// such as an intermediary router going down. With multiplexed transports, <see
    /// cref="IceRpcException.ApplicationErrorCode" /> is set to the error code provided to <see
    /// cref="IMultiplexedConnection.CloseAsync" />.</summary>
    ConnectionAborted,

    /// <summary>The connection was idle and timed-out.</summary>
    ConnectionIdle,

    /// <summary>The peer refused the connection.</summary>
    ConnectionRefused,

    /// <summary>An internal error occurred.</summary>
    InternalError,

    /// <summary>A call that was ongoing when the underlying resource (connection, stream) is aborted by the resource
    /// disposal.</summary>
    OperationAborted,

    /// <summary>An other unspecified error occurred.</summary>
    Unspecified,
}

/// <summary>This exception is thrown by transport implementation to report errors in a transport-independent manner.
/// Transport implementations should wrap transport-specific exceptions with this exception.</summary>
public class IceRpcException : IOException
{
    /// <summary>Gets the application protocol error code. It's set when this exception is triggered by the closure of a
    /// multiplexed connection by the remote peer. <see cref = "IceRpcError" /> is <see
    /// cref="IceRpcError.ConnectionAborted" /> in this situation. In all other situations, this property is null.
    /// The remote peer specifies the application error code when calling <see cref=
    /// "IMultiplexedConnection.CloseAsync" />.</summary>
    public ulong? ApplicationErrorCode { get; }

    /// <summary>Gets the transport error code.</summary>
    public IceRpcError IceRpcError { get; }

    /// <summary>Constructs a new instance of the <see cref="IceRpcException" /> class with a specified error
    /// code.</summary>
    /// <param name="errorCode">The error code.</param>
    public IceRpcException(IceRpcError errorCode)
        : base($"{nameof(IceRpcException)} {{ ErrorCode = {errorCode} }}") => IceRpcError = errorCode;

    /// <summary>Constructs a new instance of the <see cref="IceRpcException" /> class with a specified error
    /// code and message.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The message.</param>
    public IceRpcException(IceRpcError errorCode, string message)
        : base(message) => IceRpcError = errorCode;

    /// <summary>Constructs a new instance of the <see cref="IceRpcException" /> class with a specified error
    /// code and application error code.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="applicationErrorCode">The application error code.</param>
    public IceRpcException(IceRpcError errorCode, ulong applicationErrorCode)
        : base($"{nameof(IceRpcException)} {{ ErrorCode = {errorCode}, ApplicationErrorCode = {applicationErrorCode} }}")
    {
        IceRpcError = errorCode;
        ApplicationErrorCode = applicationErrorCode;
    }

    /// <summary>Constructs a new instance of the <see cref="IceRpcException" /> class with a specified error code
    /// and a reference to the inner exception that is the cause of this exception.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public IceRpcException(IceRpcError errorCode, Exception innerException)
        : base($"{nameof(IceRpcException)} {{ ErrorCode = {errorCode} }}", innerException) => IceRpcError = errorCode;

    /// <summary>Constructs a new instance of the <see cref="IceRpcException"/> class with a specified error code,
    /// application error code and a reference to the inner exception that is the cause of this exception.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="applicationErrorCode">The application error code.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public IceRpcException(IceRpcError errorCode, ulong applicationErrorCode, Exception innerException)
        : base(
            $"{nameof(IceRpcException)} {{ ErrorCode = {errorCode}, ApplicationErrorCode = {applicationErrorCode} }}",
            innerException)
    {
        IceRpcError = errorCode;
        ApplicationErrorCode = applicationErrorCode;
    }
}
