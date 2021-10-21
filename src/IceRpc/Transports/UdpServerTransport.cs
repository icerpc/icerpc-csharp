// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Net;
using System.Net.Sockets;

using static IceRpc.Transports.Internal.UdpUtils;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport{T}"/> for the udp transport.</summary>
    public class UdpServerTransport : IServerTransport<ISimpleNetworkConnection>
    {
        private readonly UdpOptions _options;

        /// <summary>Constructs a <see cref="UdpServerTransport"/> that use the default <see cref="UdpOptions"/>.
        /// </summary>
        public UdpServerTransport() => _options = new UdpOptions();

        /// <summary>Constructs a <see cref="UdpServerTransport"/> that use the given <see cref="UdpOptions"/>.
        /// </summary>
        public UdpServerTransport(UdpOptions options) => _options = options;

        IListener<ISimpleNetworkConnection> IServerTransport<ISimpleNetworkConnection>.Listen(Endpoint endpoint)
        {
            // We are not checking endpoint.Transport. The caller decided to give us this endpoint and we assume it's
            // a udp endpoint regardless of its actual transport name.

            string? multicastInterface = endpoint.ParseUdpParams().MulticastInterface;

            if (!IPAddress.TryParse(endpoint.Host, out IPAddress? ipAddress))
            {
                throw new NotSupportedException(
                    $"endpoint '{endpoint}' cannot accept datagrams because it has a DNS name");
            }

            IPEndPoint? multicastAddress = null;
            ushort port;
            var socket = new Socket(ipAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    // TODO: Don't enable DualMode sockets on macOS, https://github.com/dotnet/corefx/issues/31182
                    socket.DualMode = !(OperatingSystem.IsMacOS() || _options.IsIPv6Only);
                }

                socket.ExclusiveAddressUse = true;

                if (_options.ReceiveBufferSize is int receiveSize)
                {
                    socket.ReceiveBufferSize = receiveSize;
                }
                if (_options.SendBufferSize is int sendSize)
                {
                    socket.SendBufferSize = sendSize;
                }

                var addr = new IPEndPoint(ipAddress, endpoint.Port);
                if (IsMulticast(ipAddress))
                {
                    multicastAddress = addr;

                    socket.ExclusiveAddressUse = false;
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                    if (OperatingSystem.IsWindows())
                    {
                        // Windows does not allow binding to the multicast address itself so we bind to the wildcard
                        // instead. As a result, bidirectional connection won't work because the source address won't
                        // be the multicast address and the client will therefore reject the datagram.
                        addr = new IPEndPoint(
                            addr.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any,
                            addr.Port);
                    }
                }

                socket.Bind(addr);

                port = (ushort)((IPEndPoint)socket.LocalEndPoint!).Port;

                if (multicastAddress != null)
                {
                    multicastAddress.Port = port;
                    SetMulticastGroup(socket, multicastInterface, multicastAddress.Address);
                }
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            // Disable the warning about Dispose not being called on UdpSocket. It is disposed by the
            // UdpListener so there's no need to dispose it before loosing scope.
#pragma warning disable CA2000
            return new UdpListener(
                new UdpSocket(socket, isServer: true, multicastAddress),
                endpoint with { Port = port });
#pragma warning restore CA2000
        }
    }
}
