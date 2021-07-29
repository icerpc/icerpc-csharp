// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the tcp and ssl transports.</summary>
    public class TcpClientTransport : IClientTransport
    {
        private readonly TcpOptions _options;

        /// <summary>Constructs a <see cref="TcpClientTransport"/> that use the default <see cref="TcpOptions"/>.
        /// </summary>
        public TcpClientTransport() => _options = new TcpOptions();

        /// <summary>Constructs a <see cref="TcpClientTransport"/> that use the given <see cref="TcpOptions"/>.
        /// </summary>
        public TcpClientTransport(TcpOptions options) => _options = options;

        MultiStreamConnection IClientTransport.CreateConnection(
             Endpoint remoteEndpoint,
             ClientConnectionOptions connectionOptions,
             ILoggerFactory loggerFactory)
        {
            // First verify all parameters:
            bool? tls = remoteEndpoint.ParseTcpParams().Tls;

            if (remoteEndpoint.Protocol == Protocol.Ice1)
            {
                tls = remoteEndpoint.Transport == TransportNames.Ssl;
            }
            else if (tls == null)
            {
                // TODO: add ability to override this default tls=true through some options
                tls = true;
                remoteEndpoint = remoteEndpoint with
                {
                    LocalParams = remoteEndpoint.LocalParams.Add(new EndpointParam("_tls", "true"))
                };
            }

            ILogger logger = loggerFactory.CreateLogger("IceRpc");

            EndPoint netEndPoint = IPAddress.TryParse(remoteEndpoint.Host, out IPAddress? ipAddress) ?
                new IPEndPoint(ipAddress, remoteEndpoint.Port) :
                new DnsEndPoint(remoteEndpoint.Host, remoteEndpoint.Port);

            // We still specify the address family for the socket if an address is set to ensure an IPv4 socket is
            // created if the address is an IPv4 address.
            Socket socket = ipAddress == null ?
                new Socket(SocketType.Stream, ProtocolType.Tcp) :
                new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                if (ipAddress?.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.DualMode = !_options.IsIPv6Only;
                }

                if (_options.LocalEndPoint is IPEndPoint localEndPoint)
                {
                    socket.Bind(localEndPoint);
                }

                socket.SetBufferSize(_options.ReceiveBufferSize,
                                     _options.SendBufferSize,
                                     remoteEndpoint.Transport,
                                     logger);
                socket.NoDelay = true;
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex);
            }

            var tcpSocket = new TcpSocket(socket, logger, tls, netEndPoint);
            return NetworkSocketConnection.FromNetworkSocket(tcpSocket, remoteEndpoint, connectionOptions, _options);
        }
    }
}
