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
            int incomingFrameMaxSize,
            bool isServer,
            CancellationToken cancel)
        {
            using IDisposable scope = _logger.StartConnectionScope(connectionInformation, isServer);

            IProtocolConnection protocolConnection = await _decoratee.CreateProtocolConnectionAsync(
                networkConnection,
                connectionInformation,
                incomingFrameMaxSize,
                isServer,
                cancel).ConfigureAwait(false);

            _logger.LogCreateProtocolConnection((Protocol)connectionInformation.LocalEndpoint.Scheme,
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
