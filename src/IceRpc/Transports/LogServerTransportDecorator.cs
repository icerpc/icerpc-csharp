// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Builds a log server transport decorator.</summary>
    public class LogServerTransportDecorator : IServerTransport
    {
        private readonly IServerTransport _decoratee;

        /// <summary>Constructs a server transport decorator. The network connections created by this server
        /// transport will log traces if <see cref="LogLevel.Trace"/> is enabled on the logger created with
        /// the transport logger factory.</summary>
        public LogServerTransportDecorator(IServerTransport decoratee) => _decoratee = decoratee;

        /// <inheritdoc/>
        public (IListener?, INetworkConnection?) Listen(Endpoint endpoint, ILoggerFactory loggerFactory)
        {
            (IListener? listener, INetworkConnection? connection) = _decoratee.Listen(endpoint, loggerFactory);
            if (connection != null && connection.Logger.IsEnabled(LogLevel.Trace))
            {
                connection = new LogNetworkConnectionDecorator(connection);
            }
            return (listener != null ? new LogListenerDecorator(listener) : null, connection);
        }
    }
}
