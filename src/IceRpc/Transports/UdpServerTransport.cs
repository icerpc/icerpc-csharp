// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport{ISimpleNetworkConnection}"/> for the udp transport.</summary>
    public class UdpServerTransport : IServerTransport<ISimpleNetworkConnection>
    {
        private readonly UdpServerOptions _options;

        /// <summary>Constructs a <see cref="UdpServerTransport"/> with the default <see cref="UdpServerOptions"/>.
        /// </summary>
        public UdpServerTransport() => _options = new UdpServerOptions();

        /// <summary>Constructs a <see cref="UdpServerTransport"/> with the specified <see cref="UdpServerOptions"/>.
        /// </summary>
        public UdpServerTransport(UdpServerOptions options) => _options = options;

        IListener<ISimpleNetworkConnection> IServerTransport<ISimpleNetworkConnection>.Listen(
            Endpoint endpoint,
            ILoggerFactory loggerFactory)
        {
            // This is the composition root of the tcp server transport, where we install log decorators when logging
            // is enabled.

            var udpServerConnection = new UdpServerNetworkConnection(endpoint, _options);

            ISimpleNetworkConnection serverConnection =
                loggerFactory.CreateLogger("IceRpc.Transports") is ILogger logger &&
                    logger.IsEnabled(UdpLoggerExtensions.MaxLogLevel) ?
                        new LogUdpNetworkConnectionDecorator(udpServerConnection, logger) : udpServerConnection;

            return new UdpListener(udpServerConnection.LocalEndpoint, serverConnection);
        }
    }
}
