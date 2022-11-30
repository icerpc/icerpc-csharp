// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Sockets;

namespace IceRpc.Transports.Internal;

internal static class SocketExceptionExtensions
{
    /// <summary>Converts a socket error into a <see cref="IceRpcError" />.</summary>
    internal static IceRpcError ToTransportErrorCode(this SocketError socketError) =>
        socketError switch
        {
            SocketError.AddressAlreadyInUse => IceRpcError.AddressInUse,
            SocketError.ConnectionAborted => IceRpcError.ConnectionAborted,
            // Shutdown matches EPIPE and ConnectionReset matches ECONNRESET. Both are the result of the peer closing
            // non-gracefully the connection. EPIPE is returned if the socket is closed and the send buffer is empty
            // while ECONNRESET is returned if the send buffer is not empty.
            SocketError.ConnectionReset => IceRpcError.ConnectionAborted,
            SocketError.Shutdown => IceRpcError.ConnectionAborted,
            SocketError.ConnectionRefused => IceRpcError.ConnectionRefused,
            SocketError.OperationAborted => IceRpcError.OperationAborted,
            _ => IceRpcError.IceRpcError
        };
}
