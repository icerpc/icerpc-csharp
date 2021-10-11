// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Builds a log client transport decorator.</summary>
    public class LogClientTransportDecorator : IClientTransport
    {
        private readonly IClientTransport _decoratee;
        private readonly ILogger _logger;

        /// <summary>Constructs a client transport decorator. The network connections created by this client
        /// transport will log traces if <see cref="LogLevel.Trace"/> is enabled on the logger created with
        /// the logger factory.</summary>
        /// <param name="decoratee">The client transport to decorate.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        public LogClientTransportDecorator(IClientTransport decoratee, ILoggerFactory loggerFactory)
        {
            _decoratee = decoratee;
            _logger = loggerFactory.CreateLogger("IceRpc.Transports");
        }

        /// <inheritdoc/>
        public INetworkConnection CreateConnection(Endpoint remoteEndpoint)
        {
            INetworkConnection connection = _decoratee.CreateConnection(remoteEndpoint);
            if (connection is NetworkSocketConnection networkSocketConnection)
            {
                return new LogNetworkSocketConnectionDecorator(networkSocketConnection, isServer: false, _logger);
            }
            else
            {
                return new LogNetworkConnectionDecorator(connection, isServer: false, _logger);
            }
        }
    }
}
