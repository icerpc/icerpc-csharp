// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

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

    /// <summary>Gets the IceRpc error.</summary>
    public IceRpcError IceRpcError { get; }

    /// <summary>Constructs a new instance of the <see cref="IceRpcException" /> class with a specified error.
    /// </summary>
    /// <param name="error">The IceRpc error.</param>
    public IceRpcException(IceRpcError error)
        : this(error, $"{nameof(IceRpcException)} {{ IceRpcError = {error} }}")
    {
    }

    /// <summary>Constructs a new instance of the <see cref="IceRpcException" /> class with a specified error
    /// and message.</summary>
    /// <param name="error">The error code.</param>
    /// <param name="message">The message.</param>
    public IceRpcException(IceRpcError error, string message)
        : base(message) => IceRpcError = error;

    /// <summary>Constructs a new instance of the <see cref="IceRpcException" /> class with a specified error and
    /// application error code.</summary>
    /// <param name="error">The error.</param>
    /// <param name="applicationErrorCode">The application error code.</param>
    public IceRpcException(IceRpcError error, ulong applicationErrorCode)
        : base($"{nameof(IceRpcException)} {{ IceRpcError = {error}, ApplicationErrorCode = {applicationErrorCode} }}")
    {
        IceRpcError = error;
        ApplicationErrorCode = applicationErrorCode;
    }

    /// <summary>Constructs a new instance of the <see cref="IceRpcException" /> class with a specified error and a
    /// reference to the inner exception that is the cause of this exception.</summary>
    /// <param name="error">The error code.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public IceRpcException(IceRpcError error, Exception innerException)
        : base($"{nameof(IceRpcException)} {{ IceRpcError = {error} }}", innerException) => IceRpcError = error;

    /// <summary>Constructs a new instance of the <see cref="IceRpcException"/> class with a specified error,
    /// application error code and a reference to the inner exception that is the cause of this exception.</summary>
    /// <param name="error">The error.</param>
    /// <param name="applicationErrorCode">The application error code.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public IceRpcException(IceRpcError error, ulong applicationErrorCode, Exception innerException)
        : base(
            $"{nameof(IceRpcException)} {{ IceRpcError = {error}, ApplicationErrorCode = {applicationErrorCode} }}",
            innerException)
    {
        IceRpcError = error;
        ApplicationErrorCode = applicationErrorCode;
    }
}
