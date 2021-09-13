// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the tcp and ssl transports.</summary>
    public class TcpClientTransport : IClientTransport
    {
        private readonly TcpOptions _tcpOptions;
        private readonly SlicOptions _slicOptions;
        private readonly SslClientAuthenticationOptions? _authenticationOptions;

        /// <summary>Constructs a <see cref="TcpClientTransport"/>.</summary>
        public TcpClientTransport() :
            this(tcpOptions: new(), slicOptions: new(), null)
        {
        }

        /// <summary>Constructs a <see cref="TcpClientTransport"/>.</summary>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        public TcpClientTransport(SslClientAuthenticationOptions authenticationOptions) :
            this(tcpOptions: new(), slicOptions: new(), authenticationOptions)
        {
        }

        /// <summary>Constructs a <see cref="TcpClientTransport"/>.</summary>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        public TcpClientTransport(
            TcpOptions tcpOptions,
            SlicOptions slicOptions,
            SslClientAuthenticationOptions? authenticationOptions)
        {
            _tcpOptions = tcpOptions;
            _slicOptions = slicOptions;
            _authenticationOptions = authenticationOptions;
        }

        INetworkConnection IClientTransport.CreateConnection(Endpoint remoteEndpoint, ILoggerFactory loggerFactory)
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
                    socket.DualMode = !_tcpOptions.IsIPv6Only;
                }

                if (_tcpOptions.LocalEndPoint is IPEndPoint localEndPoint)
                {
                    socket.Bind(localEndPoint);
                }

                socket.SetBufferSize(_tcpOptions.ReceiveBufferSize,
                                     _tcpOptions.SendBufferSize,
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
            if (remoteEndpoint.Protocol == Protocol.Ice2)
            {
                return new SlicConnection(tcpSocket, remoteEndpoint, isServer: false, logger, _slicOptions);
            }
            else
            {
                return new SocketConnection(tcpSocket, remoteEndpoint, isServer: false);
            }
        }
    }
}
