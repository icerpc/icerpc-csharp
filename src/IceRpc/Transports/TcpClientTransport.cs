// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the tcp and ssl transports.</summary>
    public class TcpClientTransport : IClientTransport
    {
        private readonly TcpOptions _options;
        private readonly SslClientAuthenticationOptions? _authenticationOptions;

        /// <summary>Constructs a <see cref="TcpClientTransport"/>.</summary>
        /// <param name="options">The transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options. If not set, ssl is disabled.</param>
        public TcpClientTransport(
            TcpOptions? options = null,
            SslClientAuthenticationOptions? authenticationOptions = null)
        {
            _options = options ?? new();
            _authenticationOptions = authenticationOptions;
        }

        MultiStreamConnection IClientTransport.CreateConnection(Endpoint remoteEndpoint, ILoggerFactory loggerFactory)
        {
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

            var tcpSocket = new TcpClientSocket(socket, logger, _authenticationOptions, netEndPoint);
            return NetworkSocketConnection.FromNetworkSocket(tcpSocket, remoteEndpoint, isServer: false, _options);
        }
    }
}
