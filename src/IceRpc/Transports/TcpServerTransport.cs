// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport{ISimpleNetworkConnection}"/> for the tcp and ssl transports.
    /// </summary>
    public class TcpServerTransport : IServerTransport<ISimpleNetworkConnection>
    {
        /// <inheritdoc/>
        public Endpoint DefaultEndpoint => "icerpc+tcp://[::0]";

        private readonly TcpServerOptions _options;

        /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
        public TcpServerTransport() :
            this(new())
        {
        }

        /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
        /// <param name="options">The transport options.</param>
        public TcpServerTransport(TcpServerOptions options) => _options = options;

        /// <inheritdoc/>
        IListener<ISimpleNetworkConnection> IServerTransport<ISimpleNetworkConnection>.Listen(
            Endpoint endpoint,
            ILogger logger)
        {
            // This is the composition root of the tcp server transport, where we install log decorators when logging
            // is enabled.

            Func<TcpServerNetworkConnection, ISimpleNetworkConnection> serverConnectionDecorator =
                logger.IsEnabled(TcpLoggerExtensions.MaxLogLevel) ?
                    connection => new LogTcpNetworkConnectionDecorator(connection, logger) : connection => connection;

            return new TcpListener(endpoint, _options, serverConnectionDecorator);
        }
    }
}
