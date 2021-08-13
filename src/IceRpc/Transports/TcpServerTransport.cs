// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the tcp and ssl transports.</summary>
    public class TcpServerTransport : IServerTransport
    {
        private readonly TcpOptions _options;

        /// <summary>Constructs a <see cref="TcpServerTransport"/> that use the default <see cref="TcpOptions"/>.
        /// </summary>
        public TcpServerTransport() => _options = new TcpOptions();

        /// <summary>Constructs a <see cref="TcpServerTransport"/> that use the given <see cref="TcpOptions"/>.
        /// </summary>
        public TcpServerTransport(TcpOptions options) => _options = options;

        (IListener?, MultiStreamConnection?) IServerTransport.Listen(
            Endpoint endpoint,
            ServerConnectionOptions connectionOptions,
            ILoggerFactory loggerFactory)
        {
            // We are not checking endpoint.Transport. The caller decided to give us this endpoint and we assume it's
            // a tcp or ssl endpoint regardless of its actual transport name.

            if (!IPAddress.TryParse(endpoint.Host, out IPAddress? ipAddress))
            {
                throw new NotSupportedException(
                    $"endpoint '{endpoint}' cannot accept connections because it has a DNS name");
            }

            ILogger logger = loggerFactory.CreateLogger("IceRpc");

            var address = new IPEndPoint(ipAddress, endpoint.Port);
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.DualMode = !_options.IsIPv6Only;
                }

                socket.ExclusiveAddressUse = true;

                socket.SetBufferSize(_options.ReceiveBufferSize,
                                     _options.SendBufferSize,
                                     endpoint.Transport,
                                     logger);

                socket.Bind(address);
                address = (IPEndPoint)socket.LocalEndPoint!;
                socket.Listen(_options.ListenerBackLog);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex);
            }

            return (new Internal.TcpListener(socket,
                                             endpoint: endpoint with { Port = (ushort)address.Port },
                                             logger,
                                             connectionOptions,
                                             _options),
                    null);
        }
    }
}
