// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport{ISimpleNetworkConnection}"/> for the udp transport.</summary>
    public class UdpServerTransport : IServerTransport<ISimpleNetworkConnection>
    {
        /// <inheritdoc/>
        public string Name => TransportNames.Udp;

        private readonly UdpServerTransportOptions _options;

        /// <summary>Constructs a <see cref="UdpServerTransport"/> with the default <see cref="UdpServerTransportOptions"/>.
        /// </summary>
        public UdpServerTransport()
            : this(new())
        {
        }

        /// <summary>Constructs a <see cref="UdpServerTransport"/> with the specified <see cref="UdpServerTransportOptions"/>.
        /// </summary>
        public UdpServerTransport(UdpServerTransportOptions options) => _options = options;

        IListener<ISimpleNetworkConnection> IServerTransport<ISimpleNetworkConnection>.Listen(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authencationOptions,
            ILogger logger)
        {
            if (authencationOptions != null)
            {
                throw new NotSupportedException("cannot create secure UDP server");
            }

            // This is the composition root of the tcp server transport, where we install log decorators when logging
            // is enabled.
#pragma warning disable CA2000 // the caller will Dispose the connection
            var udpServerConnection = new UdpServerNetworkConnection(endpoint.WithTransport(Name), _options);

            ISimpleNetworkConnection serverConnection =
                logger.IsEnabled(UdpLoggerExtensions.MaxLogLevel) ?
                    new LogUdpNetworkConnectionDecorator(udpServerConnection, logger) : udpServerConnection;

            return new UdpListener(udpServerConnection.LocalEndpoint, serverConnection);
        }
    }
}
