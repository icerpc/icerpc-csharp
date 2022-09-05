// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Sockets;

namespace IceRpc.Transports.Internal;

internal static class SocketExceptionExtensions
{
    /// <summary>Converts a socket exception into a <see cref="ConnectFailedException"/>.</summary>
    internal static Exception ToConnectFailedException(this Exception exception)
    {
        SocketException socketException =
            exception as SocketException ??
            exception.InnerException as SocketException ??
            throw new ConnectFailedException(exception);

        return socketException.SocketErrorCode == SocketError.ConnectionRefused ?
            new ConnectionRefusedException(exception) :
            new ConnectFailedException(exception);
    }

    /// <summary>Converts a socket exception into a <see cref="TransportException"/> or
    /// <see cref="ConnectionLostException"/>.</summary>
    internal static Exception ToTransportException(this Exception exception)
    {
        // TODO: see #1712, this should likely just throw the transport exception without wrapping it in
        // ConnectionLostException which might be wrapped again with another ConnectionLostException by the protocol
        // connection implementation.

        SocketException socketException =
            exception as SocketException ??
            exception.InnerException as SocketException ??
            throw new ConnectionLostException(exception);

        SocketError error = socketException.SocketErrorCode;
        if (error == SocketError.ConnectionReset ||
            error == SocketError.Shutdown ||
            error == SocketError.ConnectionAborted ||
            error == SocketError.NetworkDown ||
            error == SocketError.NetworkReset)
        {
            return new ConnectionLostException(exception);
        }
        else
        {
            return new TransportException(exception);
        }
    }
}
