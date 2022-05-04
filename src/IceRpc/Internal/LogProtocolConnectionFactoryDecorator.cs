// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Buffers;

namespace IceRpc.Internal
{
    /// <summary>A log decorator for protocol connection factory.</summary>
    internal class LogProtocolConnectionFactoryDecorator<T, TOptions> : IProtocolConnectionFactory<T, TOptions>
        where T : INetworkConnection
        where TOptions : class
    {
        private readonly IProtocolConnectionFactory<T, TOptions> _decoratee;
        private readonly ILogger _logger;

        async Task<IProtocolConnection> IProtocolConnectionFactory<T, TOptions>.CreateProtocolConnectionAsync(
            T networkConnection,
            NetworkConnectionInformation connectionInformation,
            IDispatcher dispatcher,
            Action<Dictionary<ConnectionFieldKey, ReadOnlySequence<byte>>>? onConnect,
            bool isServer,
            TOptions? protocolOptions,
            CancellationToken cancel)
        {
            using IDisposable scope = _logger.StartConnectionScope(connectionInformation, isServer);

            IProtocolConnection protocolConnection = await _decoratee.CreateProtocolConnectionAsync(
                networkConnection,
                connectionInformation,
                dispatcher,
                onConnect,
                isServer,
                protocolOptions,
                cancel).ConfigureAwait(false);

            _logger.LogCreateProtocolConnection(
                protocolConnection.Protocol,
                connectionInformation.LocalEndPoint,
                connectionInformation.RemoteEndPoint);

            return new LogProtocolConnectionDecorator(
                protocolConnection,
                connectionInformation,
                isServer,
                _logger);
        }

        internal LogProtocolConnectionFactoryDecorator(
            IProtocolConnectionFactory<T, TOptions> decoratee,
            ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
