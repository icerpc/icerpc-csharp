// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Provides a log decorator for protocol connections.</summary>
internal class MetricsProtocolConnectionDecorator : IProtocolConnection
{
    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    private readonly bool _logStart;
    private readonly IProtocolConnection _decoratee;
    private readonly Metrics _metrics;
    private TransportConnectionInformation? _connectionInformation;
    private Task? _shutdownTask;

    public async Task<(TransportConnectionInformation ConnectionInformation, Task ShutdownRequested)> ConnectAsync(
        CancellationToken cancellationToken)
    {
        if (_logStart)
        {
            _metrics.ConnectStart();
        }
        try
        {
            (_connectionInformation, Task shutdownRequested) = await _decoratee.ConnectAsync(cancellationToken)
                .ConfigureAwait(false);
            _metrics.ConnectSuccess();
            return (_connectionInformation, shutdownRequested);
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
        if (_connectionInformation is not null)
        {
            if (_shutdownTask is null)
            {
                // Disposing a connection without calling shutdown first count as an error.
                _metrics.ConnectionFailure();
            }
            _metrics.ConnectionDiconnected();
        }
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
        _decoratee.InvokeAsync(request, cancellationToken);

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            _shutdownTask = _decoratee.ShutdownAsync(cancellationToken);
            await _shutdownTask.ConfigureAwait(false);
        }
        catch
        {
            _metrics.ConnectionFailure();
            throw;
        }
    }

    /// <summary>A protocol connection decorator to log connection metrics.</summary>
    /// <param name="decoratee">The protocol connection decoratee.</param>
    /// <param name="metrics">The metrics object used to log the metrics.</param>
    /// <param name="logStart">Whether or not to log the ConnectionStart and ConnectStart events. For server connections
    /// these events are logged from a separate <see cref="Server.IConnector"/> decorator.</param>
    internal MetricsProtocolConnectionDecorator(
        IProtocolConnection decoratee,
        Metrics metrics,
        bool logStart)
    {
        _logStart = logStart;
        _decoratee = decoratee;
        _metrics = metrics;
    }
}
