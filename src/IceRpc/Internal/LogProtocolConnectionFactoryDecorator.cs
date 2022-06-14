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

        async Task<(IProtocolConnection, NetworkConnectionInformation)> IProtocolConnectionFactory<T>.CreateConnectionAsync(
            T networkConnection,
            bool isServer,
            ConnectionOptions connectionOptions,
            Action onIdle,
            Action<string> onShutdown,
            CancellationToken cancel)
        {
            (IProtocolConnection connection, NetworkConnectionInformation information) =
                await _decoratee.CreateConnectionAsync(
                    networkConnection,
                    isServer,
                    connectionOptions,
                    onIdle,
                    onShutdown,
                    cancel).ConfigureAwait(false);

            using IDisposable scope = _logger.StartConnectionScope(information, isServer);
            _logger.LogProtocolConnectionConnect(
                connection.Protocol,
                information.LocalEndPoint,
                information.RemoteEndPoint);

            return (new LogProtocolConnectionDecorator(connection, information, isServer, _logger), information);
        }

        internal LogProtocolConnectionFactoryDecorator(IProtocolConnectionFactory<T> decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
