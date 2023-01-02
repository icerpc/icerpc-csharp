// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>Provides a decorator that adds logging to the <see cref="IProtocolConnection" />.</summary>
internal class LogProtocolConnectionDecorator : IProtocolConnection
{
    public Task<Exception?> Closed => _decoratee.Closed;

    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    public Task ShutdownRequested => _decoratee.ShutdownRequested;

    private bool IsServer => _remoteNetworkAddress is not null;

    private readonly IProtocolConnection _decoratee;

    private readonly ILogger _logger;

    private volatile Task? _logShutdownTask;

    private readonly EndPoint? _remoteNetworkAddress;

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            TransportConnectionInformation connectionInformation = await _decoratee.ConnectAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogConnectionConnected(
                IsServer,
                connectionInformation.LocalNetworkAddress,
                connectionInformation.RemoteNetworkAddress);

            // We only log the shutdown or completion of the connection after a successful ConnectAsync.
            _logShutdownTask = LogShutdownAsync(connectionInformation);

            return connectionInformation;
        }
        catch (Exception exception)
        {
            if (_remoteNetworkAddress is null)
            {
                _logger.LogConnectionConnectFailed(ServerAddress, exception);
            }
            else
            {
                _logger.LogConnectionConnectFailed(ServerAddress, _remoteNetworkAddress, exception);
            }
            throw;
        }

        async Task LogShutdownAsync(TransportConnectionInformation connectionInformation)
        {
            if (await Closed.ConfigureAwait(false) is Exception exception)
            {
                _logger.LogConnectionFailed(
                    IsServer,
                    connectionInformation.LocalNetworkAddress,
                    connectionInformation.RemoteNetworkAddress,
                    exception);
            }
            else
            {
                _logger.LogConnectionShutdown(
                    IsServer,
                    connectionInformation.LocalNetworkAddress,
                    connectionInformation.RemoteNetworkAddress);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _decoratee.DisposeAsync().ConfigureAwait(false);

        if (_logShutdownTask is Task logShutdownTask)
        {
            await logShutdownTask.ConfigureAwait(false);
        }
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
        _decoratee.InvokeAsync(request, cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken) => _decoratee.ShutdownAsync(cancellationToken);

    internal LogProtocolConnectionDecorator(
        IProtocolConnection decoratee,
        EndPoint? remoteNetworkAddress,
        ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
        _remoteNetworkAddress = remoteNetworkAddress;
    }
}
