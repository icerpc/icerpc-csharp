// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the tcp and ssl transports.</summary>
    public class TcpServerTransport : IServerTransport
    {
        private readonly TcpOptions _tcpOptions;
        private readonly SlicOptions _slicOptions;
        private readonly SslServerAuthenticationOptions? _authenticationOptions;

        /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
        public TcpServerTransport() :
            this(tcpOptions: new(), slicOptions: new(), null)
        {
        }

        /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
        /// <param name="authenticationOptions">The ssl authentication options. If not set, ssl is disabled.</param>
        public TcpServerTransport(SslServerAuthenticationOptions authenticationOptions) :
            this(tcpOptions: new(), slicOptions: new(), authenticationOptions)
        {
        }

        /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options. If not set, ssl is disabled.</param>
        public TcpServerTransport(
            TcpOptions tcpOptions,
            SlicOptions slicOptions,
            SslServerAuthenticationOptions? authenticationOptions)
        {
            _tcpOptions = tcpOptions;
            _slicOptions = slicOptions;
            _authenticationOptions = authenticationOptions;
        }

        IListener IServerTransport.Listen(Endpoint endpoint)
        {
            // We are not checking endpoint.Transport. The caller decided to give us this endpoint and we assume it's
            // a tcp or ssl endpoint regardless of its actual transport name.

            if (!IPAddress.TryParse(endpoint.Host, out IPAddress? ipAddress))
            {
                throw new NotSupportedException(
                    $"endpoint '{endpoint}' cannot accept connections because it has a DNS name");
            }

            var address = new IPEndPoint(ipAddress, endpoint.Port);
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.DualMode = !_tcpOptions.IsIPv6Only;
                }

                socket.ExclusiveAddressUse = true;

                if (_tcpOptions.ReceiveBufferSize is int receiveSize)
                {
                    socket.ReceiveBufferSize = receiveSize;
                }
                if (_tcpOptions.SendBufferSize is int sendSize)
                {
                    socket.SendBufferSize = sendSize;
                }

                socket.Bind(address);
                address = (IPEndPoint)socket.LocalEndPoint!;
                socket.Listen(_tcpOptions.ListenerBackLog);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex);
            }

            SslServerAuthenticationOptions? authenticationOptions = null;
            if (_authenticationOptions != null)
            {
                // Add the endpoint protocol to the SSL application protocols (used by TLS ALPN)
                authenticationOptions = _authenticationOptions.Clone();
                authenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol>
                    {
                        new SslApplicationProtocol(endpoint.Protocol.Name)
                    };
            }

            return new Internal.TcpListener(socket,
                                            endpoint: endpoint with { Port = (ushort)address.Port },
                                            _tcpOptions.IdleTimeout,
                                            _slicOptions,
                                            authenticationOptions);
        }
    }
}
