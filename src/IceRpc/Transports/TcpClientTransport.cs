// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{ISimpleNetworkConnection}"/> for the tcp and ssl transports.
    /// </summary>
    public class TcpClientTransport : IClientTransport<ISimpleNetworkConnection>
    {
        private readonly TcpClientOptions _options;

        /// <summary>Constructs a <see cref="TcpClientTransport"/>.</summary>
        public TcpClientTransport() :
            this(options: new())
        {
        }

        /// <summary>Constructs a <see cref="TcpClientTransport"/>.</summary>
        /// <param name="options">The transport options.</param>
        public TcpClientTransport(TcpClientOptions options) => _options = options;

        /// <inheritdoc/>
        ISimpleNetworkConnection IClientTransport<ISimpleNetworkConnection>.CreateConnection(
            Endpoint remoteEndpoint,
            ILoggerFactory loggerFactory)
        {
            // This is the composition root of the tcp client transport, where we install log decorators when logging
            // is enabled.
            var clientConnection = new TcpClientNetworkConnection(remoteEndpoint, _options);

            return loggerFactory.CreateLogger("IceRpc.Transports") is ILogger logger &&
                logger.IsEnabled(TcpLoggerExtensions.MaxLogLevel) ?
                    new LogTcpNetworkConnectionDecorator(clientConnection, logger) : clientConnection;
        }
    }
}
