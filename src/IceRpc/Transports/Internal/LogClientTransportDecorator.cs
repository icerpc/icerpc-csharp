// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    /// <summary>Builds a log client transport decorator.</summary>
    internal class LogClientTransportDecorator : IClientTransport
    {
        private readonly IClientTransport _decoratee;
        private readonly ILogger _logger;

        /// <inheritdoc/>
        public INetworkConnection CreateConnection(Endpoint remoteEndpoint)
        {
            INetworkConnection connection = _decoratee.CreateConnection(remoteEndpoint);
            if (connection is NetworkSocketConnection networkSocketConnection)
            {
                return new LogNetworkSocketConnectionDecorator(
                    networkSocketConnection,
                    isServer: false,
                    remoteEndpoint,
                    _logger);
            }
            else
            {
                return new LogNetworkConnectionDecorator(connection, isServer: false, remoteEndpoint, _logger);
            }
        }

        /// <summary>Constructs a client transport decorator to log traces.</summary>
        /// <param name="decoratee">The client transport to decorate.</param>
        /// <param name="logger">The logger.</param>
        internal LogClientTransportDecorator(IClientTransport decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
