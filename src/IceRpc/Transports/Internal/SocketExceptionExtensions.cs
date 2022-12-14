// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Sockets;

namespace IceRpc.Transports.Internal;

internal static class SocketExceptionExtensions
{
    /// <summary>Converts a SocketException into an <see cref="IceRpcException" />.</summary>
    internal static IceRpcException ToIceRpcException(this SocketException exception, string? message = null) =>
        exception.SocketErrorCode switch
        {
            SocketError.AddressAlreadyInUse => new IceRpcException(IceRpcError.AddressInUse, message, exception),
            SocketError.ConnectionAborted => new IceRpcException(IceRpcError.ConnectionAborted, message, exception),
            // Shutdown matches EPIPE and ConnectionReset matches ECONNRESET. Both are the result of the peer closing
            // non-gracefully the connection. EPIPE is returned if the socket is closed and the send buffer is empty
            // while ECONNRESET is returned if the send buffer is not empty.
            SocketError.ConnectionReset => new IceRpcException(IceRpcError.ConnectionAborted, message, exception),
            SocketError.Shutdown => new IceRpcException(IceRpcError.ConnectionAborted, message, exception),
            SocketError.ConnectionRefused => new IceRpcException(IceRpcError.ConnectionRefused, message, exception),
            SocketError.OperationAborted => new IceRpcException(IceRpcError.OperationAborted, message, exception),
            _ => new IceRpcException(IceRpcError.IceRpcError, message, exception)
        };
}
