// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport{ISimpleNetworkConnection}"/> for the udp transport.</summary>
    public class UdpServerTransport : IServerTransport<ISimpleNetworkConnection>
    {
        private readonly UdpOptions _options;

        /// <summary>Constructs a <see cref="UdpServerTransport"/> that use the default <see cref="UdpOptions"/>.
        /// </summary>
        public UdpServerTransport() => _options = new UdpOptions();

        /// <summary>Constructs a <see cref="UdpServerTransport"/> that use the given <see cref="UdpOptions"/>.
        /// </summary>
        public UdpServerTransport(UdpOptions options) => _options = options;

        IListener<ISimpleNetworkConnection> IServerTransport<ISimpleNetworkConnection>.Listen(
            Endpoint endpoint,
            ILoggerFactory loggerFactory)
        {
            // This is the composition root of the tcp server transport, where we install log decorators when logging
            // is enabled.

            Func<UdpServerNetworkConnection, ISimpleNetworkConnection> serverConnectionDecorator =
                loggerFactory.CreateLogger("IceRpc.Transports") is ILogger logger && logger.IsEnabled(LogLevel.Error) ?
                    connection => new LogUdpNetworkConnectionDecorator(connection, logger) : connection => connection;

            return new UdpListener(endpoint, _options, serverConnectionDecorator);
        }
    }
}
