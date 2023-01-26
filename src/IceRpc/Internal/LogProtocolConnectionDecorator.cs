// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;

namespace IceRpc.Internal;

/// <summary>Provides a decorator that adds logging to the <see cref="IProtocolConnection" />.</summary>
internal class LogProtocolConnectionDecorator : IProtocolConnection
{
    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    private bool IsServer => _remoteNetworkAddress is not null;

    private volatile TransportConnectionInformation? _connectionInformation;

    private readonly IProtocolConnection _decoratee;

    private readonly ILogger _logger;

    private readonly EndPoint? _remoteNetworkAddress;

    private volatile Task? _shutdownTask;

    public async Task<(TransportConnectionInformation ConnectionInformation, Task ShutdownRequested)> ConnectAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            (_connectionInformation, Task shutdownRequested) = await _decoratee.ConnectAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogConnectionConnected(
                IsServer,
                _connectionInformation.LocalNetworkAddress,
                _connectionInformation.RemoteNetworkAddress);

            return (_connectionInformation, shutdownRequested);
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
    }

    // TODO: add log for Dispose when shutdown was not called or failed
    public async ValueTask DisposeAsync()
    {
        await _decoratee.DisposeAsync().ConfigureAwait(false);

        // Wait for _shutdownTask's completion
        if (_shutdownTask is Task shutdownTask)
        {
            try
            {
                await shutdownTask.ConfigureAwait(false);
            }
            catch
            {
                // observe and ignore any exception
            }
        }
        else if (_connectionInformation is TransportConnectionInformation connectionInformation)
        {
            _logger.LogConnectionFailed(
                IsServer,
                connectionInformation.LocalNetworkAddress,
                connectionInformation.RemoteNetworkAddress,
                new ObjectDisposedException("")); // temporary, for now means disposed after connect without a shutdown
        }
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
        _decoratee.InvokeAsync(request, cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            _shutdownTask = PerformShutdownAsync(_decoratee.ShutdownAsync(cancellationToken));
            return _shutdownTask;
        }
        // catch exceptions thrown synchronously by _decoratee.ShutdownAsync
        catch (InvalidOperationException)
        {
            // Thrown if ConnectAsync wasn't called, or if ShutdownAsync is called twice.
            throw;
        }
        catch (Exception exception)
        {
            LogShutdownFailed(exception);
            throw;
        }

        void LogShutdownFailed(Exception exception)
        {
            Debug.Assert(_connectionInformation is not null);

            _logger.LogConnectionFailed(
                IsServer,
                _connectionInformation.LocalNetworkAddress,
                _connectionInformation.RemoteNetworkAddress,
                exception);
        }

        async Task PerformShutdownAsync(Task decorateeShutdownTask)
        {
            try
            {
                await decorateeShutdownTask.ConfigureAwait(false);

                Debug.Assert(_connectionInformation is not null);

                _logger.LogConnectionShutdown(
                    IsServer,
                    _connectionInformation.LocalNetworkAddress,
                    _connectionInformation.RemoteNetworkAddress);
            }
            catch (InvalidOperationException)
            {
                // See above. A decorator can convert synchronous exceptions into asynchronous exceptions.
                throw;
            }
            catch (Exception exception)
            {
                LogShutdownFailed(exception);
                throw;
            }
        }
    }

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
