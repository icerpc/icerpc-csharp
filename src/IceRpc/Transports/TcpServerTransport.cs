// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the tcp and ssl transports.</summary>
    public class TcpServerTransport : IServerTransport
    {
        private readonly TcpOptions _options;
        private readonly SslServerAuthenticationOptions? _authenticationOptions;

        /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
        /// <param name="options">The transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options. If not set, ssl is disabled.</param>
        public TcpServerTransport(
            TcpOptions? options = null,
            SslServerAuthenticationOptions? authenticationOptions = null)
        {
            _options = options ?? new();
            _authenticationOptions = authenticationOptions;
        }

        (IListener?, MultiStreamConnection?) IServerTransport.Listen(Endpoint endpoint, ILoggerFactory loggerFactory)
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
                                             _options,
                                             _authenticationOptions),
                    null);
        }
    }
}
