// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>The possible error codes carried by a <see cref="ConnectFailedException"/>. The error code specifies the
/// reason of the connection establishment failure.</summary>
public enum ConnectFailedErrorCode
{
    /// <summary>The connection establishment was canceled.</summary>
    Canceled,

    /// <summary>The transport connection establishment failed. The reason of the failure is indicated by the inner
    /// exception of the <see cref="ConnectFailedException"/>.</summary>
    TransportError,

    /// <summary>The connection establishment was refused by the server.</summary>
    Refused,
}

/// <summary>This exception reports a failed attempt to establish a connection.</summary>
public class ConnectFailedException : Exception
{
    /// <summary>Gets the connect failed error code.</summary>
    public ConnectFailedErrorCode ErrorCode { get; }

    /// <summary>Constructs a new instance of the <see cref="ConnectFailedException"/> class with a specified error
    /// code.</summary>
    /// <param name="errorCode">The error code.</param>
    public ConnectFailedException(ConnectFailedErrorCode errorCode)
        : base("connection establishment failed") => ErrorCode = errorCode;

    /// <summary>Constructs a new instance of the <see cref="ConnectFailedException"/> class with a specified error
    /// code and inner exception.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ConnectFailedException(ConnectFailedErrorCode errorCode, Exception? innerException)
        : base("connection establishment failed", innerException) => ErrorCode = errorCode;
}

/// <summary>The possible error codes carried by a <see cref="ConnectionAbortedException"/>. The error code specifies
/// the reason of the connection abortion.</summary>
public enum ConnectionAbortedErrorCode
{
    /// <summary>The connection establishment was canceled.</summary>
    ConnectCanceled,

    /// <summary>The connection establishment failed.</summary>
    ConnectFailed,

    /// <summary>The connection shutdown was canceled.</summary>
    ShutdownCanceled,

    /// <summary>The connection shutdown failed.</summary>
    ShutdownFailed,

    /// <summary>The connection was disposed.</summary>
    Disposed,
}

/// <summary>This exception indicates that a connection operation was aborted.</summary>
public class ConnectionAbortedException : Exception
{
    /// <summary>Gets the connection closed error code.</summary>
    public ConnectionAbortedErrorCode ErrorCode { get; }

    /// <summary>Constructs a new instance of the <see cref="ConnectionAbortedException"/> class with a specified error
    /// code.</summary>
    /// <param name="errorCode">The error code.</param>
    public ConnectionAbortedException(ConnectionAbortedErrorCode errorCode)
        : base("the connection is aborted") => ErrorCode = errorCode;

    /// <summary>Constructs a new instance of the <see cref="ConnectionAbortedException"/> class with a specified
    /// error code and message.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The message that describes the error.</param>
    public ConnectionAbortedException(ConnectionAbortedErrorCode errorCode, string message)
        : base(message) => ErrorCode = errorCode;

    /// <summary>Constructs a new instance of the <see cref="ConnectionAbortedException"/> class with a specified
    /// error code and inner exception.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ConnectionAbortedException(ConnectionAbortedErrorCode errorCode, Exception? innerException)
        : base("the connection is aborted", innerException) => ErrorCode = errorCode;
}

/// <summary>The possible error codes carried by a <see cref="ConnectionClosedException"/>. The error code specifies the
/// reason of the connection closure.</summary>
public enum ConnectionClosedErrorCode
{
    /// <summary>The connection was idle.</summary>
    Idle,

    /// <summary>The connection was lost.</summary>
    Lost,

    /// <summary>The connection was shutdown.</summary>
    Shutdown,

    /// <summary>The connection was shutdown by the peer.</summary>
    ShutdownByPeer,
}

/// <summary>This exception indicates that a previous established connection is closed. It is safe to retry a request
/// that failed with this exception.</summary>
public class ConnectionClosedException : Exception
{
    /// <summary>Gets the connection closed error code.</summary>
    public ConnectionClosedErrorCode ErrorCode { get; }

    /// <summary>Constructs a new instance of the <see cref="ConnectionClosedException"/> class with a specified error
    /// code.</summary>
    /// <param name="errorCode">The error code.</param>
    public ConnectionClosedException(ConnectionClosedErrorCode errorCode)
        : base("the connection is closed") => ErrorCode = errorCode;

    /// <summary>Constructs a new instance of the <see cref="ConnectionClosedException"/> class with a specified error
    /// code and message.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The message.</param>
    public ConnectionClosedException(ConnectionClosedErrorCode errorCode, string message)
        : base(message) => ErrorCode = errorCode;

    /// <summary>Constructs a new instance of the <see cref="ConnectionClosedException"/> class with a specified error
    /// code and inner exception.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ConnectionClosedException(ConnectionClosedErrorCode errorCode, Exception innerException)
        : base("the connection is closed", innerException) => ErrorCode = errorCode;
}
