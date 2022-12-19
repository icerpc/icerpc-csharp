// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>Provides a decorator that adds logging to the <see cref="IProtocolConnection" />.</summary>
internal class LogProtocolConnectionDecorator : IProtocolConnection
{
    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    public Task<Exception?> ShutdownComplete => _decoratee.ShutdownComplete;

    private bool IsServer => _remoteNetworkAddress is not null;

    private readonly TaskCompletionSource<TransportConnectionInformation?> _connectionInformationTcs = new();
    private readonly IProtocolConnection _decoratee;

    private readonly ILogger _logger;
    private readonly Task _logShutdownTask;
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

            _connectionInformationTcs.SetResult(connectionInformation);
            return connectionInformation;
        }
        catch (Exception exception)
        {
            _connectionInformationTcs.TrySetResult(null);

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
    }

    public async ValueTask DisposeAsync()
    {
        await _decoratee.DisposeAsync().ConfigureAwait(false);

        // If the application did not call ConnectASync at all, we mark ConnectAsync as unsuccessful:
        _connectionInformationTcs.TrySetResult(null);
        await _logShutdownTask.ConfigureAwait(false);
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
        _decoratee.InvokeAsync(request, cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        _decoratee.ShutdownAsync(cancellationToken);

    internal LogProtocolConnectionDecorator(
        IProtocolConnection decoratee,
        EndPoint? remoteNetworkAddress,
        ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
        _remoteNetworkAddress = remoteNetworkAddress;

        _logShutdownTask = LogShutdownAsync();

        // This task executes once per decorated connection.
        async Task LogShutdownAsync()
        {
            if (await _connectionInformationTcs.Task.ConfigureAwait(false) is
                TransportConnectionInformation connectionInformation)
            {
                // We only log a shutdown message after ConnectAsync completed successfully.

                if (await ShutdownComplete.ConfigureAwait(false) is Exception exception)
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
    }
}
