// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>This exception indicates that a connection was aborted.</summary>
public class ConnectionAbortedException : Exception
{
    /// <summary>Constructs a new instance of the <see cref="ConnectionAbortedException"/> class.</summary>
    public ConnectionAbortedException()
        : base("the connection is aborted")
    {
    }

    /// <summary>Constructs a new instance of the <see cref="ConnectionAbortedException"/> class with a specified
    /// error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public ConnectionAbortedException(string message)
        : base(message)
    {
    }

    /// <summary>Constructs a new instance of the <see cref="ConnectionAbortedException"/> class with a specified
    /// error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ConnectionAbortedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The possible error codes carried by a <see cref="ConnectionClosedException"/>.</summary>
public enum ConnectionClosedErrorCode
{
    /// <summary>The connection was disposed.</summary>
    Disposed,

    /// <summary>The connection was idle.</summary>
    Idle,

    /// <summary>The connection was shutdown.</summary>
    Shutdown,

    /// <summary>The connection was shutdown by the peer.</summary>
    ShutdownByPeer,

    /// <summary>The connection was lost.</summary>
    Lost
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
