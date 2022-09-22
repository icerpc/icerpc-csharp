// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>The possible error codes carried by a <see cref="ConnectionException"/>. The error code specifies the
/// reason of the connection failure.</summary>
public enum ConnectionErrorCode
{
    /// <summary>The connection establishment was refused by the server.</summary>
    ConnectRefused,

    /// <summary>The connection is closed because of a previous transport error or because it was shutdown or
    /// idle.</summary>
    Closed,

    /// <summary>The connection establishment, shutdown or disposal triggered triggered the cancellation of the
    /// operation.</summary>
    OperationCanceled,

    /// <summary>The connection establishment or shutdown failed because of a transport error. The <see
    /// cref="Exception.InnerException"/> is set to the <see cref="TransportException"/> that triggered the
    /// error.</summary>
    TransportError,

    /// <summary>The connection establishment or shutdown failed because of an unexpected error. The <see
    /// cref="Exception.InnerException"/> is set to the exception that triggered the error.</summary>
    Unexpected,
}

/// <summary>This exception reports a failed attempt to establish a connection.</summary>
public class ConnectionException : Exception
{
    /// <summary>Gets the connect failed error code.</summary>
    public ConnectionErrorCode ErrorCode { get; }

    /// <summary>Constructs a new instance of the <see cref="ConnectionException"/> class with a specified error
    /// code.</summary>
    /// <param name="errorCode">The error code.</param>
    public ConnectionException(ConnectionErrorCode errorCode)
        : base($"{nameof(ConnectionException)} {{ ErrorCode = {errorCode} }}") => ErrorCode = errorCode;

    /// <summary>Constructs a new instance of the <see cref="ConnectionException"/> class with a specified error code
    /// and message.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The message.</param>
    public ConnectionException(ConnectionErrorCode errorCode, string message)
        : base(message) => ErrorCode = errorCode;

    /// <summary>Constructs a new instance of the <see cref="ConnectionException"/> class with a specified error
    /// code and inner exception.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ConnectionException(ConnectionErrorCode errorCode, Exception? innerException)
        : base($"{nameof(ConnectionException)} {{ ErrorCode = {errorCode} }}", innerException) =>
        ErrorCode = errorCode;
}
