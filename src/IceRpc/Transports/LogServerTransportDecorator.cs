// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Builds a log server transport decorator.</summary>
    public class LogServerTransportDecorator : IServerTransport
    {
        private readonly IServerTransport _decoratee;
        private readonly ILogger _logger;

        /// <summary>Constructs a server transport decorator. The network connections created by this server
        /// transport will log traces if <see cref="LogLevel.Trace"/> is enabled on the logger created with
        /// the logger factory.</summary>
        /// <param name="decoratee">The server transport to decorate.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        public LogServerTransportDecorator(IServerTransport decoratee, ILoggerFactory loggerFactory)
        {
            _decoratee = decoratee;
            _logger = loggerFactory.CreateLogger("IceRpc.Transports");
        }

        /// <inheritdoc/>
        public (IListener?, INetworkConnection?) Listen(Endpoint endpoint)
        {
            (IListener? listener, INetworkConnection? connection) = _decoratee.Listen(endpoint);
            return (listener != null ? new LogListenerDecorator(listener, _logger) : null,
                    connection != null ?
                        new LogNetworkConnectionDecorator(connection, isServer: true, endpoint, _logger) :
                        null);
        }
    }
}
