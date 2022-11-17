// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal;

internal static class SocketExceptionExtensions
{
    /// <summary>Converts an exception from a socket operation into a <see cref="TransportException" />.</summary>
    internal static Exception ToTransportException(this Exception exception)
    {
        SocketException socketException =
            exception as SocketException ??
            exception.InnerException as SocketException ??
            throw ExceptionUtil.Throw(exception);

        TransportErrorCode transportErrorCode = socketException.SocketErrorCode switch
        {
            // Shutdown matches EPIPE and ConnectionReset matches ECONNRESET. Both are the result of the peer closing
            // non-gracefully the connection. EPIPE is returned if the socket is closed and the send buffer is empty
            // while ECONNRESET is returned if the send buffer is not empty.
            SocketError.ConnectionReset => TransportErrorCode.ConnectionReset,
            SocketError.Shutdown => TransportErrorCode.ConnectionReset,
            SocketError.NotConnected => TransportErrorCode.ConnectionReset,
            SocketError.ConnectionRefused => TransportErrorCode.ConnectionRefused,
            SocketError.AddressAlreadyInUse => TransportErrorCode.AddressInUse,
            SocketError.OperationAborted => TransportErrorCode.OperationAborted,
            _ => TransportErrorCode.Unspecified
        };

        return new TransportException(transportErrorCode, exception);
    }
}
