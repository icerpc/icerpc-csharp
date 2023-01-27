// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Sockets;

namespace IceRpc.Transports.Internal;

internal static class SocketExceptionExtensions
{
    /// <summary>Converts a SocketException into an <see cref="IceRpcException" />.</summary>
    internal static IceRpcException ToIceRpcException(this SocketException exception, Exception? innerException = null)
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
