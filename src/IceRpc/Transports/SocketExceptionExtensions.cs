// Copyright (c) ZeroC, Inc.

using System.Net.Sockets;

namespace IceRpc.Transports;

/// <summary>Provides an extension method to convert a <see cref="SocketException"/> into an
/// <see cref="IceRpcException"/>.</summary>
public static class SocketExceptionExtensions
{
    /// <summary>Converts a <see cref="SocketException"/> into an <see cref="IceRpcException" />.</summary>
    /// <param name="exception">The exception to convert.</param>
    /// <param name="innerException">The inner exception for the <see cref="IceRpcException"/>, when
    /// <see langword="null"/> <paramref name="exception"/> is used as the inner exception.</param>
    /// <returns>The <see cref="IceRpcException"/> created from the <see cref="SocketException"/>.</returns>
    public static IceRpcException ToIceRpcException(this SocketException exception, Exception? innerException = null)
    {
        innerException ??= exception;
        IceRpcError errorCode = exception.SocketErrorCode switch
        {
            SocketError.AddressAlreadyInUse => IceRpcError.AddressInUse,
            SocketError.ConnectionAborted => IceRpcError.ConnectionAborted,
            // Shutdown matches EPIPE and ConnectionReset matches ECONNRESET. Both are the result of the peer closing
            // non-gracefully the connection. EPIPE is returned if the socket is closed and the send buffer is empty
            // while ECONNRESET is returned if the send buffer is not empty.
            SocketError.ConnectionReset => IceRpcError.ConnectionAborted,
            SocketError.HostUnreachable => IceRpcError.ServerUnreachable,
            SocketError.NetworkUnreachable => IceRpcError.ServerUnreachable,
            SocketError.Shutdown => IceRpcError.ConnectionAborted,
            SocketError.ConnectionRefused => IceRpcError.ConnectionRefused,
            SocketError.OperationAborted => IceRpcError.OperationAborted,
            _ => IceRpcError.IceRpcError
        };

        return new IceRpcException(errorCode, innerException);
    }
}
