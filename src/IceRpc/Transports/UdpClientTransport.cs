// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static IceRpc.Transports.Internal.UdpUtils;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{T}"/> for the UDP transport.</summary>
    public class UdpClientTransport : IClientTransport<ISimpleNetworkConnection>
    {
        private readonly UdpOptions _options;

        /// <summary>Constructs a <see cref="UdpClientTransport"/> that use the default <see cref="UdpOptions"/>.
        /// </summary>
        public UdpClientTransport() => _options = new UdpOptions();

        /// <summary>Constructs a <see cref="UdpClientTransport"/> that use the given <see cref="UdpOptions"/>.
        /// </summary>
        public UdpClientTransport(UdpOptions options) => _options = options;

        ISimpleNetworkConnection IClientTransport<ISimpleNetworkConnection>.CreateConnection(Endpoint remoteEndpoint)
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

                if (_options.ReceiveBufferSize is int receiveSize)
                {
                    socket.ReceiveBufferSize = receiveSize;
                }
                if (_options.SendBufferSize is int sendSize)
                {
                    socket.SendBufferSize = sendSize;
                }
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex);
            }

            return new SocketNetworkConnection(
                new UdpSocket(socket, isServer: false, netEndPoint, ttl, multicastInterface),
                remoteEndpoint,
                isServer: false,
                _options.IdleTimeout);
        }
    }
}
