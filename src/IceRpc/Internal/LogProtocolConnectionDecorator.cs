// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;

namespace IceRpc.Internal;

/// <summary>Provides a decorator that adds logging to the <see cref="IProtocolConnection" />.</summary>
internal class LogProtocolConnectionDecorator : IProtocolConnection
{
    private bool IsServer => _remoteNetworkAddress is not null;

    // _connectionInformation is not volatile because all correct callers of IProtocolConnection.ConnectAsync wait for
    // the connection establishment to complete (successfully or not) before calling any other method on
    // IProtocolConnection, including DisposeAsync.
    private TransportConnectionInformation? _connectionInformation;

    private readonly IProtocolConnection _decoratee;

    private readonly ILogger _logger;

    private readonly EndPoint? _remoteNetworkAddress;

    private readonly ServerAddress _serverAddress;

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
                _logger.LogConnectionConnectFailed(_serverAddress, exception);
            }
            else
            {
                _logger.LogConnectionConnectFailed(_serverAddress, _remoteNetworkAddress, exception);
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _decoratee.DisposeAsync().ConfigureAwait(false);

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

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _decoratee.ShutdownAsync(cancellationToken).ConfigureAwait(false);

            Debug.Assert(_connectionInformation is not null);

            _logger.LogConnectionShutdown(
                IsServer,
                _connectionInformation.LocalNetworkAddress,
                _connectionInformation.RemoteNetworkAddress);
        }
        catch (Exception exception) when (_connectionInformation is not null)
        {
            _logger.LogConnectionShutdownFailed(
                IsServer,
                _connectionInformation.LocalNetworkAddress,
                _connectionInformation.RemoteNetworkAddress,
                exception);
            throw;
        }
    }

    internal LogProtocolConnectionDecorator(
        IProtocolConnection decoratee,
        ServerAddress serverAddress,
        EndPoint? remoteNetworkAddress,
        ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
        _remoteNetworkAddress = remoteNetworkAddress;
        _serverAddress = serverAddress;
    }
}
