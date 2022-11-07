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
        private EndPoint? _localNetworkAddress;
        private readonly ILogger _logger;
        private readonly Task _logShutdownAsync;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                TransportConnectionInformation result = await _decoratee.ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);

                _localNetworkAddress = result.LocalNetworkAddress;
                _logger.ClientConnectSucceed(ServerAddress, _localNetworkAddress, result.RemoteNetworkAddress);
                return result;
            }
            catch (Exception exception)
            {
                _logger.ClientConnectFailed(ServerAddress, exception);
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
                    if (_localNetworkAddress is not null)
                    {
                        // We only log Shutdown when the ConnectAsync completed successfully.
                        _logger.ClientConnectionShutdown(ServerAddress, _localNetworkAddress);
                    }
                }
                catch (Exception exception)
                {
                    Debug.Assert(_localNetworkAddress is not null);
                    _logger.ClientConnectionFailure(ServerAddress, _localNetworkAddress, exception);
                }
            }
        }
    }
}
