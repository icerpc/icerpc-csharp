// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the tcp and ssl transports.</summary>
    public class TcpClientTransport : SimpleClientTransport
    {
        private readonly TcpOptions _tcpOptions;
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
            SslClientAuthenticationOptions? authenticationOptions) :
            base(slicOptions, tcpOptions.IdleTimeout)
        {
            _tcpOptions = tcpOptions;
            _authenticationOptions = authenticationOptions;
        }

        /// <inheritdoc/>
        protected override INetworkConnection CreateConnection(Endpoint remoteEndpoint)
        {
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

                if (_tcpOptions.ReceiveBufferSize is int receiveSize)
                {
                    socket.ReceiveBufferSize = receiveSize;
                }
                if (_tcpOptions.SendBufferSize is int sendSize)
                {
                    socket.SendBufferSize = sendSize;
                }

                socket.NoDelay = true;
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex);
            }

            return new SocketNetworkConnection(
                new TcpClientSocket(socket, _authenticationOptions, netEndPoint),
                remoteEndpoint,
                isServer: false,
                _tcpOptions.IdleTimeout);
        }
    }
}
