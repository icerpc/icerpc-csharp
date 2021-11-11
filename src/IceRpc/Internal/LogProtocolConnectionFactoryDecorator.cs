// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>A log decorator for protocol connection factory.</summary>
    internal class LogProtocolConnectionFactoryDecorator<T> : IProtocolConnectionFactory<T> where T : INetworkConnection
    {
        private readonly IProtocolConnectionFactory<T> _decoratee;
        private readonly ILogger _logger;

        async Task<(IProtocolConnection, NetworkConnectionInformation)> IProtocolConnectionFactory<T>.CreateProtocolConnectionAsync(
            T networkConnection,
            int incomingFrameMaxSize,
            bool isServer,
            CancellationToken cancel)
        {
            (IProtocolConnection protocolConnection, NetworkConnectionInformation connectionInformation) =
                await _decoratee.CreateProtocolConnectionAsync(networkConnection,
                                                               incomingFrameMaxSize,
                                                               isServer,
                                                               cancel).ConfigureAwait(false);

            _logger.LogCreateProtocolConnection(connectionInformation.LocalEndpoint.Protocol);

            return (new LogProtocolConnectionDecorator(protocolConnection, connectionInformation, isServer, _logger),
                    connectionInformation);
        }

        internal LogProtocolConnectionFactoryDecorator(IProtocolConnectionFactory<T> decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
