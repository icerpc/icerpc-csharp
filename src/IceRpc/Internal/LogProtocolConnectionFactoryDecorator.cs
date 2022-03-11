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

        async Task<IProtocolConnection> IProtocolConnectionFactory<T>.CreateProtocolConnectionAsync(
            T networkConnection,
            NetworkConnectionInformation connectionInformation,
            IDictionary<ConnectionFieldKey, OutgoingFieldValue> localFields,
            bool isServer,
            CancellationToken cancel)
        {
            using IDisposable scope = _logger.StartConnectionScope(connectionInformation, isServer);

            IProtocolConnection protocolConnection = await _decoratee.CreateProtocolConnectionAsync(
                networkConnection,
                connectionInformation,
                localFields,
                isServer,
                cancel).ConfigureAwait(false);

            _logger.LogCreateProtocolConnection(connectionInformation.LocalEndpoint.Protocol,
                                                connectionInformation.LocalEndpoint,
                                                connectionInformation.RemoteEndpoint);

            return new LogProtocolConnectionDecorator(protocolConnection, connectionInformation, isServer, _logger);
        }

        internal LogProtocolConnectionFactoryDecorator(IProtocolConnectionFactory<T> decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
