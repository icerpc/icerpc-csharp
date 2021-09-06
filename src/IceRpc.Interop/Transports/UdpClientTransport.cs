// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static IceRpc.Transports.Internal.UdpUtils;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the UDP transport.</summary>
    public class UdpClientTransport : IClientTransport
    {
        private readonly UdpOptions _options;

        /// <summary>Constructs a <see cref="UdpClientTransport"/> that use the default <see cref="UdpOptions"/>.
        /// </summary>
        public UdpClientTransport() => _options = new UdpOptions();

        /// <summary>Constructs a <see cref="UdpClientTransport"/> that use the given <see cref="UdpOptions"/>.
        /// </summary>
        public UdpClientTransport(UdpOptions options) => _options = options;

        MultiStreamConnection IClientTransport.CreateConnection(Endpoint remoteEndpoint, ILoggerFactory loggerFactory)
        {
            // We are not checking endpoint.Transport. The caller decided to give us this endpoint and we assume it's
            // a udp endpoint regardless of its actual transport name.

            (bool _, int ttl, string? multicastInterface) = remoteEndpoint.ParseUdpParams();

            EndPoint netEndPoint = IPAddress.TryParse(remoteEndpoint.Host, out IPAddress? ipAddress) ?
                new IPEndPoint(ipAddress, remoteEndpoint.Port) :
                new DnsEndPoint(remoteEndpoint.Host, remoteEndpoint.Port);

            if (multicastInterface == "*")
            {
                throw new NotSupportedException(
                    $"endpoint '{remoteEndpoint}' cannot use interface '*' to send datagrams");
            }

            ILogger logger = loggerFactory.CreateLogger("IceRpc");
            Socket socket = ipAddress == null ?
                new Socket(SocketType.Dgram, ProtocolType.Udp) :
                new Socket(ipAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                if (netEndPoint is IPEndPoint ipEndpoint && IsMulticast(ipEndpoint.Address))
                {
                    if (ipAddress?.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        socket.DualMode = !_options.IsIPv6Only;
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

                if (_options.LocalEndPoint is IPEndPoint localEndPoint)
                {
                    socket.Bind(localEndPoint);
                }

                socket.SetBufferSize(_options.ReceiveBufferSize,
                                     _options.SendBufferSize,
                                     remoteEndpoint.Transport,
                                     logger);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex);
            }

#pragma warning disable CA2000 // Dispose objects before losing scope
            var udpSocket = new UdpSocket(socket, logger, isServer: false, netEndPoint, ttl, multicastInterface);
#pragma warning restore CA2000 // Dispose objects before losing scope
            return NetworkSocketConnection.FromNetworkSocket(udpSocket, remoteEndpoint, isServer: false, new());
        }
    }
}
