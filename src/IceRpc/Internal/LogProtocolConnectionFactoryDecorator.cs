// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>A log decorator for protocol connection factory.</summary>
    internal class LogProtocolConnectionFactoryDecorator<T> : IProtocolConnectionFactory<T> where T : INetworkConnection
    {
        private readonly IProtocolConnectionFactory<T> _decoratee;
        private readonly Endpoint _endpoint;
        private readonly ILogger _logger;

        async Task<(IProtocolConnection, NetworkConnectionInformation)> IProtocolConnectionFactory<T>.CreateProtocolConnectionAsync(
            T networkConnection,
            int incomingFrameMaxSize,
            bool isServer,
            CancellationToken cancel)
        {
            using IDisposable scope = _logger.StartNewConnectionScope(_endpoint, isServer);

            (IProtocolConnection protocolConnection, NetworkConnectionInformation connectionInformation) =
                await _decoratee.CreateProtocolConnectionAsync(networkConnection,
                                                            incomingFrameMaxSize,
                                                            isServer,
                                                            cancel).ConfigureAwait(false);

            _logger.LogCreateProtocolConnection(connectionInformation.LocalEndpoint.Protocol,
                                                connectionInformation.LocalEndpoint,
                                                connectionInformation.RemoteEndpoint);

            return (new LogProtocolConnectionDecorator(protocolConnection, connectionInformation, isServer, _logger),
                    connectionInformation);
        }

        internal LogProtocolConnectionFactoryDecorator(
            IProtocolConnectionFactory<T> decoratee,
            Endpoint endpoint,
            ILogger logger)
        {
            _decoratee = decoratee;
            _endpoint = endpoint;
            _logger = logger;
        }
    }
}
