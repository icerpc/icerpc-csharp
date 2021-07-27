// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using IceRpc.Transports.Internal;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the tcp and ssl transports.</summary>
    public class TcpServerTransport : IServerTransport
    {
        public (IListener?, MultiStreamConnection?) Listen(
            Endpoint endpoint,
            ServerConnectionOptions options,
            ILogger logger)
        {
            // We are not checking endpoint.Transport. The caller decided to give us this endpoint and we assume it's
            // a tcp or ssl endpoint regardless of its actual transport name.

            if (!IPAddress.TryParse(endpoint.Host, out IPAddress? ipAddress))
            {
                throw new NotSupportedException(
                    $"endpoint '{endpoint}' cannot accept connections because it has a DNS name");
            }

            var address = new IPEndPoint(ipAddress, endpoint.Port);
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                TcpOptions tcpOptions = options.TransportOptions as TcpOptions ?? TcpOptions.Default;
                if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.DualMode = !tcpOptions.IsIPv6Only;
                }

                socket.ExclusiveAddressUse = true;

                socket.SetBufferSize(tcpOptions.ReceiveBufferSize, tcpOptions.SendBufferSize, endpoint.Transport, logger);

                socket.Bind(address);
                address = (IPEndPoint)socket.LocalEndPoint!;
                socket.Listen(tcpOptions.ListenerBackLog);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex);
            }

            return (new Internal.TcpListener(socket, endpoint: endpoint with { Port = (ushort)address.Port }, logger, options), null);
        }
    }
}
