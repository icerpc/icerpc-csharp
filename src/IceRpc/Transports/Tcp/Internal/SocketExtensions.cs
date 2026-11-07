// Copyright (c) ZeroC, Inc.

using System.Net.Sockets;

namespace IceRpc.Transports.Tcp.Internal;

internal static class SocketExtensions
{
    /// <summary>Configures a socket.</summary>
    internal static void Configure(this Socket socket, bool noDelay, int? receiveBufferSize, int? sendBufferSize)
    {
        socket.NoDelay = noDelay;

        if (receiveBufferSize is int receiveSize)
        {
            socket.ReceiveBufferSize = receiveSize;
        }
        if (sendBufferSize is int sendSize)
        {
            socket.SendBufferSize = sendSize;
        }
    }
}
