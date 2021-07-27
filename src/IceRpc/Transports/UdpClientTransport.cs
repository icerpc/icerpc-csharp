// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using IceRpc.Transports.Internal;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using static IceRpc.Transports.Internal.UdpUtils;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the UDP transport.</summary>
    public class UdpClientTransport : IClientTransport
    {
        /// <inheritdoc/>
        public MultiStreamConnection CreateConnection(
            EndpointRecord remoteEndpoint,
            ClientConnectionOptions options,
            ILogger logger)
        {
            // We are not checking endpoint.Transport. The caller decided to give us this endpoint and we assume it's
            // a udp endpoint regardless of its actual transport ID.

            _ = ParseUdpParameters(remoteEndpoint); // can throw FormatException
            (int ttl, string? multicastInterface) = ParseLocalUdpParameters(remoteEndpoint);

            EndPoint netEndPoint = IPAddress.TryParse(remoteEndpoint.Host, out IPAddress? ipAddress) ?
                new IPEndPoint(ipAddress, remoteEndpoint.Port) :
                new DnsEndPoint(remoteEndpoint.Host, remoteEndpoint.Port);

            if (multicastInterface == "*")
            {
                throw new NotSupportedException(
                    $"endpoint '{remoteEndpoint}' cannot use interface '*' to send datagrams");
            }

            Socket socket = ipAddress == null ?
                new Socket(SocketType.Dgram, ProtocolType.Udp) :
                new Socket(ipAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                UdpOptions udpOptions = options.TransportOptions as UdpOptions ?? UdpOptions.Default;
                if (netEndPoint is IPEndPoint ipEndpoint && IsMulticast(ipEndpoint.Address))
                {
                    if (ipAddress?.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        socket.DualMode = !udpOptions.IsIPv6Only;
                    }

                    // IP multicast socket options require a socket created with the correct address family.
                    if (multicastInterface != null)
                    {
                        Debug.Assert(multicastInterface.Length > 0);
                        if (ipAddress?.AddressFamily == AddressFamily.InterNetwork)
                        {
                            socket.SetSocketOption(
                                SocketOptionLevel.IP,
                                SocketOptionName.MulticastInterface,
                                GetIPv4InterfaceAddress(multicastInterface).GetAddressBytes());
                        }
                        else
                        {
                            socket.SetSocketOption(
                                SocketOptionLevel.IPv6,
                                SocketOptionName.MulticastInterface,
                                GetIPv6InterfaceIndex(multicastInterface));
                        }
                    }

                    if (ttl != -1)
                    {
                        socket.Ttl = (short)ttl;
                    }
                }

                if (udpOptions.LocalEndPoint is IPEndPoint localEndPoint)
                {
                    socket.Bind(localEndPoint);
                }

                socket.SetBufferSize(udpOptions.ReceiveBufferSize,
                                     udpOptions.SendBufferSize,
                                     remoteEndpoint.Transport,
                                     logger);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex);
            }

            var udpSocket = new UdpSocket(socket, logger, isServer: false, netEndPoint, ttl, multicastInterface);
            return NetworkSocketConnection.FromNetworkSocket(udpSocket, remoteEndpoint.ToString(), options);
        }
    }
}
