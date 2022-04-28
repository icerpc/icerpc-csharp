// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Buffers;

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
            Configure.ConnectionOptions connectionOptions,
            Action<Dictionary<ConnectionFieldKey, ReadOnlySequence<byte>>>? onConnect,
            bool isServer,
            CancellationToken cancel)
        {
            using IDisposable scope = _logger.StartConnectionScope(connectionInformation, isServer);

            IProtocolConnection protocolConnection = await _decoratee.CreateProtocolConnectionAsync(
                networkConnection,
                connectionInformation,
                connectionOptions,
                onConnect,
                isServer,
                cancel).ConfigureAwait(false);

            // TODO: do we need this parameter?
            Protocol protocol = protocolConnection is IceRpcProtocolConnection ? Protocol.IceRpc : Protocol.Ice;

            _logger.LogCreateProtocolConnection(
                protocol,
                connectionInformation.LocalEndPoint,
                connectionInformation.RemoteEndPoint);

            return new LogProtocolConnectionDecorator(
                protocolConnection,
                protocol,
                connectionInformation,
                isServer,
                _logger);
        }

        internal LogProtocolConnectionFactoryDecorator(IProtocolConnectionFactory<T> decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
