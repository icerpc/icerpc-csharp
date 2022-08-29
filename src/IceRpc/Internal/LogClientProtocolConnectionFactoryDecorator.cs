// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Net;

namespace IceRpc.Internal;

/// <summary>Provides a log decorator for client protocol connection factory.</summary>
// TODO: should we make this class public?
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

        public Task<string> ShutdownComplete => _decoratee.ShutdownComplete;

        private EndPoint? _clientNetworkAddress;

        private readonly IProtocolConnection _decoratee;

        private readonly Task _logShutdownAsync;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            ClientEventSource.Log.ConnectStart(ServerAddress);
            try
            {
                TransportConnectionInformation result = await _decoratee.ConnectAsync(cancellationToken)
                    .ConfigureAwait(false);
                _clientNetworkAddress = result.LocalNetworkAddress;
                ClientEventSource.Log.ConnectSuccess(ServerAddress, _clientNetworkAddress!);
                return result;
            }
            catch (Exception exception)
            {
                ClientEventSource.Log.ConnectFailure(ServerAddress, exception);
                throw;
            }
            finally
            {
                ClientEventSource.Log.ConnectStop(ServerAddress, _clientNetworkAddress);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _decoratee.DisposeAsync().ConfigureAwait(false);
            await _logShutdownAsync.ConfigureAwait(false); // make sure the task completes before ConnectionStop
            ClientEventSource.Log.ConnectionStop(ServerAddress, _clientNetworkAddress);
        }

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
            _decoratee.InvokeAsync(request, cancellationToken);

        public void OnAbort(Action<Exception> callback) => _decoratee.OnAbort(callback);

        public void OnShutdown(Action<string> callback) => _decoratee.OnShutdown(callback);

        public Task ShutdownAsync(string message, CancellationToken cancellationToken = default) =>
            _decoratee.ShutdownAsync(message, cancellationToken);

        internal LogProtocolConnectionDecorator(IProtocolConnection decoratee)
        {
            _decoratee = decoratee;
            _logShutdownAsync = LogShutdownAsync();

            async Task LogShutdownAsync()
            {
                try
                {
                    string message = await ShutdownComplete.ConfigureAwait(false);
                    ClientEventSource.Log.ConnectionShutdown(ServerAddress, _clientNetworkAddress!, message);
                }
                catch (Exception exception)
                {
                    ClientEventSource.Log.ConnectionFailure(ServerAddress, _clientNetworkAddress, exception);
                }
            }
        }
    }
}
