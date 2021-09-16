// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

using static IceRpc.Transports.Internal.UdpUtils;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the udp transport.</summary>
    public class UdpServerTransport : IServerTransport
    {
        private readonly UdpOptions _options;

        /// <summary>Constructs a <see cref="UdpServerTransport"/> that use the default <see cref="UdpOptions"/>.
        /// </summary>
        public UdpServerTransport() => _options = new UdpOptions();

        /// <summary>Constructs a <see cref="UdpServerTransport"/> that use the given <see cref="UdpOptions"/>.
        /// </summary>
        public UdpServerTransport(UdpOptions options) => _options = options;

        (IListener?, INetworkConnection?) IServerTransport.Listen(Endpoint endpoint, ILoggerFactory loggerFactory)
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
            ILogger logger = loggerFactory.CreateLogger("IceRpc.Transports");
            var socket = new Socket(ipAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    // TODO: Don't enable DualMode sockets on macOS, https://github.com/dotnet/corefx/issues/31182
                    socket.DualMode = !(OperatingSystem.IsMacOS() || _options.IsIPv6Only);
                }

                socket.ExclusiveAddressUse = true;

                socket.SetBufferSize(_options.ReceiveBufferSize,
                                     _options.SendBufferSize,
                                     endpoint.Transport,
                                     logger);

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

            return (
                null,
                LogNetworkConnectionDecorator.Create(
                    new NetworkSocketConnection(
                        new UdpSocket(socket, isServer: true, multicastAddress),
                        endpoint with { Port = port },
                        isServer: true,
                        idleTimeout: TimeSpan.MaxValue,
                        new(),
                        logger),
                    logger));
        }
    }
}
