// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Net;

namespace IceRpc.Internal;

/// <summary>Provides a log decorator for client protocol connection factory.</summary>
internal class LogClientProtocolConnectionFactoryDecorator : IClientProtocolConnectionFactory
{
    private readonly IClientProtocolConnectionFactory _decoratee;

    public IProtocolConnection CreateConnection(ServerAddress serverAddress)
    {
        IProtocolConnection connection = _decoratee.CreateConnection(serverAddress);
        ClientEventSource.Log.ConnectionStart(connection.ServerAddress);
        return new LogProtocolConnectionDecorator(connection);
    }

    internal LogClientProtocolConnectionFactoryDecorator(IClientProtocolConnectionFactory decoratee) =>
        _decoratee = decoratee;

    /// <summary>Provides a log decorator for client protocol connections.</summary>
    private class LogProtocolConnectionDecorator : IProtocolConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public Task ShutdownComplete => _decoratee.ShutdownComplete;

        private readonly IProtocolConnection _decoratee;
        private EndPoint? _localNetworkAddress;
        private readonly Task _logShutdownAsync;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            ClientEventSource.Log.ConnectStart(ServerAddress);
            try
            {
                TransportConnectionInformation result = await _decoratee.ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);

                _localNetworkAddress = result.LocalNetworkAddress;
                ClientEventSource.Log.ConnectSuccess(ServerAddress, _localNetworkAddress, result.RemoteNetworkAddress);
                return result;
            }
            catch (Exception exception)
            {
                ClientEventSource.Log.ConnectFailure(ServerAddress, exception);
                throw;
            }
            finally
            {
                ClientEventSource.Log.ConnectStop(ServerAddress, _localNetworkAddress);
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

        internal LogProtocolConnectionDecorator(IProtocolConnection decoratee)
        {
            _decoratee = decoratee;
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
                        ClientEventSource.Log.ConnectionShutdown(ServerAddress, _localNetworkAddress);
                    }
                }
                catch (Exception exception)
                {
                    ClientEventSource.Log.ConnectionFailure(ServerAddress, _localNetworkAddress, exception);
                }
                ClientEventSource.Log.ConnectionStop(ServerAddress, _localNetworkAddress);
            }
        }
    }
}
