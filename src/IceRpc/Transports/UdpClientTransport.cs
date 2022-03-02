// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{ISimpleNetworkConnection}"/> for the UDP transport.</summary>
    public class UdpClientTransport : IClientTransport<ISimpleNetworkConnection>
    {
        private readonly UdpClientTransportOptions _options;

        /// <summary>Constructs a <see cref="UdpClientTransport"/> with the default <see cref="UdpClientTransportOptions"/>.
        /// </summary>
        public UdpClientTransport()
            : this(new())
        {
        }

        /// <summary>Constructs a <see cref="UdpClientTransport"/> with the specified <see cref="UdpClientTransportOptions"/>.
        /// </summary>
        public UdpClientTransport(UdpClientTransportOptions options) => _options = options;

        ISimpleNetworkConnection IClientTransport<ISimpleNetworkConnection>.CreateConnection(
            Endpoint remoteEndpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            ILogger logger)
        {
            if (authenticationOptions != null)
            {
                throw new NotSupportedException("cannot create a secure UDP connection");
            }

            // This is the composition root of the udp client transport, where we install log decorators when logging
            // is enabled.
            var clientConnection = new UdpClientNetworkConnection(remoteEndpoint, _options);

            return logger.IsEnabled(UdpLoggerExtensions.MaxLogLevel) ?
                new LogUdpNetworkConnectionDecorator(clientConnection, logger) : clientConnection;
        }
    }
}
