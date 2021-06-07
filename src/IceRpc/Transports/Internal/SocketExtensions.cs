// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Sockets;

namespace IceRpc.Internal
{
    internal static class SocketExtensions
    {
        internal static void CloseNoThrow(this Socket socket)
        {
            try
            {
                socket.Close();
            }
            catch (SocketException)
            {
                // Ignore
            }
        }
    }
}
