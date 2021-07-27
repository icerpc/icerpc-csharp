// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

using static IceRpc.Transports.Internal.UdpUtils;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the udp transport.</summary>
    public class UdpServerTransport : IServerTransport
    {
        /// <inheritdoc/>
        public (IListener?, MultiStreamConnection?) Listen(
            Endpoint endpoint,
            ServerConnectionOptions options,
            ILogger logger)
        {
            // We are not checking endpoint.Transport. The caller decided to give us this endpoint and we assume it's
            // a udp endpoint regardless of its actual transport name.

            string? multicastInterface = ParseUdpParams(endpoint).MulticastInterface;

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
                UdpOptions udpOptions = options.TransportOptions as UdpOptions ?? UdpOptions.Default;

                if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    // TODO: Don't enable DualMode sockets on macOS, https://github.com/dotnet/corefx/issues/31182
                    socket.DualMode = !(OperatingSystem.IsMacOS() || udpOptions.IsIPv6Only);
                }

                socket.ExclusiveAddressUse = true;

                socket.SetBufferSize(udpOptions.ReceiveBufferSize, udpOptions.SendBufferSize, endpoint.Transport, logger);

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

            var udpSocket = new UdpSocket(socket, logger, isServer: true, multicastAddress);
            return (null,
                    NetworkSocketConnection.FromNetworkSocket(
                        udpSocket,
                        endpoint: endpoint with { Port = port },
                        options));
        }
    }
}
