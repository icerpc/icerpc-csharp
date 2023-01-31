// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Provides a log decorator for protocol connections.</summary>
internal class MetricsProtocolConnectionDecorator : IProtocolConnection
{
    private readonly bool _connectStarted;
    private readonly IProtocolConnection _decoratee;
    private bool _isConnected;
    private bool _isShutdown;
    private readonly Metrics _metrics;

    public async Task<(TransportConnectionInformation ConnectionInformation, Task ShutdownRequested)> ConnectAsync(
        CancellationToken cancellationToken)
    {
        if (!_connectStarted)
        {
            _metrics.ConnectStart();
        }
        try
        {
            (TransportConnectionInformation connectionInformation, Task shutdownRequested) =
                await _decoratee.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _metrics.ConnectSuccess();
            _isConnected = true;
            return (connectionInformation, shutdownRequested);
        }
        catch
        {
            _metrics.ConnectionFailure();
            throw;
        }
        finally
        {
            _metrics.ConnectStop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _decoratee.DisposeAsync().ConfigureAwait(false);
        if (_isConnected)
        {
            if (!_isShutdown)
            {
                // shutdown failed or didn't call shutdown at all.
                _metrics.ConnectionFailure();
            }
            _metrics.ConnectionDisconnected();
        }
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
        _decoratee.InvokeAsync(request, cancellationToken);

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await _decoratee.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        _isShutdown = true;
    }

    /// <summary>A protocol connection decorator to log connection metrics.</summary>
    /// <param name="decoratee">The protocol connection decoratee.</param>
    /// <param name="metrics">The metrics object used to log the metrics.</param>
    /// <param name="connectStarted">Whether or not the ConnectStart events has be already logged. For server connections
    /// the event is logged from a separate <see cref="Server.IConnector"/> decorator.</param>
    internal MetricsProtocolConnectionDecorator(
        IProtocolConnection decoratee,
        Metrics metrics,
        bool connectStarted)
    {
        _connectStarted = connectStarted;
        _decoratee = decoratee;
        _metrics = metrics;
    }
}
