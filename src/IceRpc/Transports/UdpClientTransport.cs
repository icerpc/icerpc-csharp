// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{ISimpleNetworkConnection}"/> for the UDP transport.</summary>
    public class UdpClientTransport : IClientTransport<ISimpleNetworkConnection>
    {
        private readonly UdpOptions _options;

        /// <summary>Constructs a <see cref="UdpClientTransport"/> that use the default <see cref="UdpOptions"/>.
        /// </summary>
        public UdpClientTransport() => _options = new UdpOptions();

        /// <summary>Constructs a <see cref="UdpClientTransport"/> that use the given <see cref="UdpOptions"/>.
        /// </summary>
        public UdpClientTransport(UdpOptions options) => _options = options;

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
