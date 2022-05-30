// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>A log decorator for protocol connection factory.</summary>
    internal class LogProtocolConnectionFactoryDecorator<T> : IProtocolConnectionFactory<T>
        where T : INetworkConnection
    {
        private readonly IProtocolConnectionFactory<T> _decoratee;
        private readonly ILogger _logger;

        IProtocolConnection IProtocolConnectionFactory<T>.CreateProtocolConnectionAsync(
            T networkConnection,
            ConnectionOptions connectionOptions) =>
            new LogProtocolConnectionDecorator(
                _decoratee.CreateProtocolConnectionAsync(networkConnection, connectionOptions),
                _logger);

        internal LogProtocolConnectionFactoryDecorator(IProtocolConnectionFactory<T> decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
