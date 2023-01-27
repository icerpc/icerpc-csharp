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

    // _connectionInformation is not volatile because all correct callers of IProtocolConnection wait for ConnectAsync
    // to complete before calling any other method on IProtocolConnection, including DisposeAsync.
    private TransportConnectionInformation? _connectionInformation;

    private readonly IProtocolConnection _decoratee;

    private readonly ILogger _logger;

    private readonly EndPoint? _remoteNetworkAddress;

    // _shutdownTask is volatile because it's possible (but rare) for ShutdownAsync and DisposeAsync to execute
    // concurrently.
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
                // ignore any exception
            }
        }

        if (_connectionInformation is not null)
        {
            _logger.LogConnectionDisposed(
                IsServer,
                _connectionInformation.LocalNetworkAddress,
                _connectionInformation.RemoteNetworkAddress);
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
        catch (Exception exception) when (_connectionInformation is not null)
        {
            LogShutdownFailed(exception, _connectionInformation);
            throw;
        }
        // otherwise ConnectAsync was not called (a bug) and the exception is an InvalidOperationException or an
        // ObjectDisposedException.

        void LogShutdownFailed(Exception exception, TransportConnectionInformation connectionInformation) =>
            _logger.LogConnectionShutdownFailed(
                IsServer,
                connectionInformation.LocalNetworkAddress,
                connectionInformation.RemoteNetworkAddress,
                exception);

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
            catch (Exception exception) when (_connectionInformation is not null)
            {
                LogShutdownFailed(exception, _connectionInformation);
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
