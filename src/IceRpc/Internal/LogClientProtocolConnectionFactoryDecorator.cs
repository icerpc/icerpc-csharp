// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;

namespace IceRpc.Internal;

/// <summary>Provides a log decorator for client protocol connection factory.</summary>
internal class LogClientProtocolConnectionFactoryDecorator : IClientProtocolConnectionFactory
{
    private readonly IClientProtocolConnectionFactory _decoratee;
    private readonly ILogger _logger;

    public IProtocolConnection CreateConnection(ServerAddress serverAddress)
    {
        IProtocolConnection connection = _decoratee.CreateConnection(serverAddress);
        return new LogProtocolConnectionDecorator(connection, _logger);
    }

    internal LogClientProtocolConnectionFactoryDecorator(
        IClientProtocolConnectionFactory decoratee,
        ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }

    /// <summary>Provides a log decorator for client protocol connections.</summary>
    private class LogProtocolConnectionDecorator : IProtocolConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public Task ShutdownComplete => _decoratee.ShutdownComplete;

        private readonly IProtocolConnection _decoratee;
        private readonly ILogger _logger;
        private readonly Task _logShutdownAsync;
        private TransportConnectionInformation? _connectionInformation;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                _connectionInformation = await _decoratee.ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);

                _logger.ConnectSucceeded(
                    isServer: false,
                    _connectionInformation.LocalNetworkAddress,
                    _connectionInformation.RemoteNetworkAddress);
                return _connectionInformation;
            }
            catch (Exception exception)
            {
                _logger.ConnectFailed(ServerAddress, exception);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _decoratee.DisposeAsync().ConfigureAwait(false);
            await _logShutdownAsync.ConfigureAwait(false);
        }

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
            _decoratee.InvokeAsync(request, cancellationToken);

        public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
            _decoratee.ShutdownAsync(cancellationToken);

        internal LogProtocolConnectionDecorator(IProtocolConnection decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
            _logShutdownAsync = LogShutdownAsync();

            // This task executes exactly once per decorated connection.
            async Task LogShutdownAsync()
            {
                try
                {
                    await ShutdownComplete.ConfigureAwait(false);
                    if (_connectionInformation is not null)
                    {
                        // We only log Shutdown when the ConnectAsync completed successfully.
                        _logger.ConnectionShutdown(
                            isServer: false,
                            _connectionInformation.LocalNetworkAddress,
                            _connectionInformation.RemoteNetworkAddress);
                    }
                }
                catch (Exception exception)
                {
                    Debug.Assert(_connectionInformation is not null);
                    _logger.ConnectionFailure(
                        isServer: false,
                        _connectionInformation.LocalNetworkAddress,
                        _connectionInformation.RemoteNetworkAddress,
                        exception);
                }
            }
        }
    }
}
