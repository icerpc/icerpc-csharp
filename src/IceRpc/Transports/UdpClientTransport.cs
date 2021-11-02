// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{ISimpleNetworkConnection}"/> for the UDP transport.</summary>
    public class UdpClientTransport : IClientTransport<ISimpleNetworkConnection>
    {
        private readonly UdpClientOptions _options;

        /// <summary>Constructs a <see cref="UdpClientTransport"/> with the default <see cref="UdpClientOptions"/>.
        /// </summary>
        public UdpClientTransport() => _options = new UdpClientOptions();

        /// <summary>Constructs a <see cref="UdpClientTransport"/> with the specified <see cref="UdpClientOptions"/>.
        /// </summary>
        public UdpClientTransport(UdpClientOptions options) => _options = options;

        ISimpleNetworkConnection IClientTransport<ISimpleNetworkConnection>.CreateConnection(
            Endpoint remoteEndpoint,
            ILoggerFactory loggerFactory)
        {
            // This is the composition root of the udp client transport, where we install log decorators when logging
            // is enabled.
            var clientConnection = new UdpClientNetworkConnection(remoteEndpoint, _options);

            return loggerFactory.CreateLogger("IceRpc.Transports") is ILogger logger &&
                logger.IsEnabled(UdpLoggerExtensions.MaxLogLevel) ?
                    new LogUdpNetworkConnectionDecorator(clientConnection, logger) : clientConnection;
        }
    }
}
