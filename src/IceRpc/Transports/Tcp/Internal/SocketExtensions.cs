// Copyright (c) ZeroC, Inc.

using System.Net.Sockets;

namespace IceRpc.Transports.Tcp.Internal;

internal static class SocketExtensions
{
    /// <summary>Extension methods for <see cref="Socket" />.</summary>
    /// <param name="socket">The socket to configure.</param>
    extension(Socket socket)
    {
        /// <summary>Configures a socket.</summary>
        internal void Configure(TcpTransportOptions options)
        {
            socket.NoDelay = options.NoDelay;

            if (options.ReceiveBufferSize is int receiveSize)
            {
                socket.ReceiveBufferSize = receiveSize;
            }
            if (options.SendBufferSize is int sendSize)
            {
                socket.SendBufferSize = sendSize;
            }
        }
    }
}
