// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    internal static class SocketExtensions
    {
        internal static void SetBufferSize(
            this Socket socket,
            int? receiveSize,
            int? sendSize,
            string transportName,
            ILogger logger)
        {
            if (receiveSize != null)
            {
                // Try to set the buffer size. The kernel will silently adjust the size to an acceptable value. Then
                // read the size back to get the size that was actually set.
                socket.ReceiveBufferSize = receiveSize.Value;
                if (socket.ReceiveBufferSize != receiveSize)
                {
                    logger.LogReceiveBufferSizeAdjusted(transportName, receiveSize.Value, socket.ReceiveBufferSize);
                }
            }

            if (sendSize != null)
            {
                // Try to set the buffer size. The kernel will silently adjust the size to an acceptable value. Then
                // read the size back to get the size that was actually set.
                socket.SendBufferSize = sendSize.Value;
                if (socket.SendBufferSize != sendSize)
                {
                    logger.LogSendBufferSizeAdjusted(transportName, sendSize.Value, socket.SendBufferSize);
                }
            }
        }
    }
}
