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
        var tcs = new TaskCompletionSource<TransportConnectionInformation?>();

        // If Connect is called and succeeds, we log the shutdown.
        _logShutdownTask = LogShutdownAsync(tcs.Task);

        try
        {
            TransportConnectionInformation connectionInformation = await _decoratee.ConnectAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogConnectionConnected(
                IsServer,
                connectionInformation.LocalNetworkAddress,
                connectionInformation.RemoteNetworkAddress);

            tcs.SetResult(connectionInformation);
            return connectionInformation;
        }
        catch (Exception exception)
        {
            _ = tcs.TrySetResult(null); // TrySetResult in case ConnectAsync is incorrectly called twice.

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

        async Task LogShutdownAsync(Task<TransportConnectionInformation?> task)
        {
            // We wait for ConnectAsync to complete and succeed (see above)
            if (await task.ConfigureAwait(false) is TransportConnectionInformation connectionInformation)
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
    }

    public async ValueTask DisposeAsync()
    {
        await _decoratee.DisposeAsync().ConfigureAwait(false);

        if (_logShutdownTask is Task logShutdownTask)
        {
            // When ConnectAsync is called, we log the ConnectFailure or Connect success + shutdown.
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
