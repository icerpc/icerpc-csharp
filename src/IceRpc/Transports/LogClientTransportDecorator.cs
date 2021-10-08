// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Builds a log client transport decorator.</summary>
    public class LogClientTransportDecorator : IClientTransport
    {
        private readonly IClientTransport _decoratee;

        /// <summary>Constructs a server transport decorator. The network connections created by this server
        /// transport will log traces if <see cref="LogLevel.Trace"/> is enabled on the logger created with
        /// the transport logger factory.</summary>
        public LogClientTransportDecorator(IClientTransport decoratee) => _decoratee = decoratee;

        /// <inheritdoc/>
        public INetworkConnection CreateConnection(Endpoint remoteEndpoint, ILoggerFactory loggerFactory)
        {
            INetworkConnection connection = _decoratee.CreateConnection(remoteEndpoint, loggerFactory);
            if (connection.Logger.IsEnabled(LogLevel.Trace))
            {
                if (connection is NetworkSocketConnection networkSocketConnection)
                {
                    return new LogNetworkSocketConnectionDecorator(networkSocketConnection, isServer: false);
                }
                else
                {
                    return new LogNetworkConnectionDecorator(connection, isServer: false);
                }
            }
            else
            {
                return connection;
            }
        }
    }
}
