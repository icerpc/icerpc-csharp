// Copyright (c) ZeroC, Inc.

using System.Net.Sockets;

namespace IceRpc.Transports.Tcp.Internal;

internal static class SocketExtensions
{
    /// <summary>Configures a socket.</summary>
    internal static void Configure(this Socket socket, TcpTransportOptions options)
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
