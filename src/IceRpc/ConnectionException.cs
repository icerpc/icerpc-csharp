// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>The possible error codes carried by a <see cref="ConnectionException" />. The error code specifies the
/// reason of the connection failure.</summary>
public enum ConnectionErrorCode
{
    /// <summary>The protocol connection was closed prior to the current call. This error typically occurs when an
    /// invoker such as <see cref="ConnectionCache" /> calls <see cref="IInvoker.InvokeAsync" /> on a cached
    /// protocol connection that was closed but was not yet cleaned up or replaced by a background thread.</summary>
    ConnectionClosed,

    /// <summary>The connection establishment was refused by the server.</summary>
    ConnectRefused,

    /// <summary>The operation was aborted because the connection was aborted.</summary>
    OperationAborted,

    /// <summary>The connection establishment or shutdown failed because of an IceRpc exception. The <see
    /// cref="Exception.InnerException" /> is set to the <see cref="IceRpc.IceRpcException" /> that caused the
    /// error.</summary>
    IceRpcException,

    /// <summary>The connection establishment or shutdown failed because of an unspecified error. The <see
    /// cref="Exception.InnerException" /> is set to the exception that caused the error.</summary>
    Unspecified,
}

/// <summary>This exception reports a connection failure.</summary>
public class ConnectionException : Exception
{
    /// <summary>Gets the connection error code.</summary>
    public ConnectionErrorCode ErrorCode { get; }

    /// <summary>Constructs a new instance of the <see cref="ConnectionException" /> class with a specified error
    /// code.</summary>
    /// <param name="errorCode">The error code.</param>
    public ConnectionException(ConnectionErrorCode errorCode)
        : base($"{nameof(ConnectionException)} {{ ErrorCode = {errorCode} }}") => ErrorCode = errorCode;

    /// <summary>Constructs a new instance of the <see cref="ConnectionException" /> class with a specified error code
    /// and message.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The message.</param>
    public ConnectionException(ConnectionErrorCode errorCode, string message)
        : base(message) => ErrorCode = errorCode;

    /// <summary>Constructs a new instance of the <see cref="ConnectionException" /> class with a specified error
    /// code and inner exception.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ConnectionException(ConnectionErrorCode errorCode, Exception? innerException)
        : base($"{nameof(ConnectionException)} {{ ErrorCode = {errorCode} }}", innerException) =>
        ErrorCode = errorCode;

    /// <summary>Constructs a new instance of the <see cref="ConnectionException" /> class with a specified error
    /// code, message and inner exception.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ConnectionException(ConnectionErrorCode errorCode, string message, Exception? innerException)
        : base(message, innerException) => ErrorCode = errorCode;
}
