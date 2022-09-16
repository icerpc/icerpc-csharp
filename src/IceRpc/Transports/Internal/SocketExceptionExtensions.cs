// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Sockets;

namespace IceRpc.Transports.Internal;

internal static class SocketExceptionExtensions
{
    /// <summary>Converts an exception from a socket operation into a <see cref="TransportException"/>.</summary>
    internal static Exception ToTransportException(this Exception exception)
    {
        SocketException socketException =
            exception as SocketException ??
            exception.InnerException as SocketException ??
            throw new TransportException(TransportErrorCode.Unspecified, exception);

        SocketError error = socketException.SocketErrorCode;
        if (error == SocketError.ConnectionReset || error == SocketError.Shutdown)
        {
            // Shutdown matches EPIPE and ConnectionReset matches ECONNRESET. Both are the result of the peer closing
            // non-gracefully the connection. EPIPE is returned if the socket is closed and the send buffer is empty
            // while ECONNRESET is returned if the send buffer is not empty.
            return new TransportException(TransportErrorCode.ConnectionReset, exception);
        }
        else if (error == SocketError.ConnectionRefused)
        {
            return new TransportException(TransportErrorCode.ConnectionRefused, exception);
        }
        else if (error == SocketError.AddressAlreadyInUse)
        {
            return new TransportException(TransportErrorCode.AddressInUse, exception);
        }
        else
        {
            return new TransportException(TransportErrorCode.Unspecified, exception);
        }
    }
}
