// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    /// <summary>Builds a log server transport decorator.</summary>
    internal class LogServerTransportDecorator : IServerTransport
    {
        private readonly IServerTransport _decoratee;
        private readonly ILogger _logger;

        /// <inheritdoc/>
        public (IListener?, INetworkConnection?) Listen(Endpoint endpoint)
        {
            (IListener? listener, INetworkConnection? connection) = _decoratee.Listen(endpoint);
            return (listener != null ? new LogListenerDecorator(listener, _logger) : null,
                    connection != null ?
                        new LogNetworkConnectionDecorator(connection, isServer: true, endpoint, _logger) :
                        null);
        }

        /// <summary>Constructs a server transport decorator to log traces.</summary>
        /// <param name="decoratee">The server transport to decorate.</param>
        /// <param name="logger">The logger.</param>
        internal LogServerTransportDecorator(IServerTransport decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
