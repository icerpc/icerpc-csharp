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

        async Task<IProtocolConnection> IProtocolConnectionFactory<T>.CreateConnectionAsync(
            T networkConnection,
            NetworkConnectionInformation networkConnectionInformation,
            bool isServer,
            ConnectionOptions connectionOptions,
            CancellationToken cancel)
        {
            IProtocolConnection connection = new LogProtocolConnectionDecorator(
                await _decoratee.CreateConnectionAsync(
                    networkConnection,
                    networkConnectionInformation,
                    isServer,
                    connectionOptions,
                    cancel).ConfigureAwait(false),
                networkConnectionInformation,
                isServer,
                _logger);

            using IDisposable scope = _logger.StartConnectionScope(networkConnectionInformation, isServer);
            _logger.LogProtocolConnectionConnect(
                connection.Protocol,
                networkConnectionInformation.LocalEndPoint,
                networkConnectionInformation.RemoteEndPoint);

            return connection;
        }

        internal LogProtocolConnectionFactoryDecorator(IProtocolConnectionFactory<T> decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
