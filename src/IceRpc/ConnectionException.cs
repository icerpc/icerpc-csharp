// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>The possible error codes carried by a <see cref="ConnectionException" />. The error code specifies the
/// reason of the connection failure.</summary>
public enum ConnectionErrorCode
{
    /// <summary>The connection was closed because it was aborted, for example by a transport error or a connect
    /// timeout.</summary>
    ClosedByAbort,

    /// <summary>The connection was closed locally because it was idle.</summary>
    ClosedByIdle,

    /// <summary>The connection was closed by the remote peer.</summary>
    ClosedByPeer,

    /// <summary>The connection was closed by a local call to shutdown or dispose.</summary>
    ClosedByShutdown,

    /// <summary>The connection establishment was refused by the server.</summary>
    ConnectRefused,

    /// <summary>The operation was aborted because the connection was aborted.</summary>
    OperationAborted,

    /// <summary>The connection establishment or shutdown failed because of a transport error. The <see
    /// cref="Exception.InnerException" /> is set to the <see cref="TransportException" /> that caused the
    /// error.</summary>
    TransportError,

    /// <summary>The connection establishment or shutdown failed because of an unspecified error. The <see
    /// cref="Exception.InnerException" /> is set to the exception that caused the error.</summary>
    Unspecified,
}

/// <summary>Provides extension methods for <see cref="ConnectionErrorCode"/>.</summary>
public static class ConnectionErrorCodeExtensions
{
    /// <summary>Checks if this error code is a Closed code.</summary>
    /// <param name="errorCode">The error code to check.</param>
    /// <returns><see langword="true"/> if <paramref name="errorCode"/> is a Closed code; otherwise,
    /// <see langword="false"/>.</returns>
    public static bool IsClosedErrorCode(this ConnectionErrorCode errorCode) =>
        errorCode >= ConnectionErrorCode.ClosedByAbort && errorCode <= ConnectionErrorCode.ClosedByShutdown;
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
