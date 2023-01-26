// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Provides a log decorator for protocol connections.</summary>
internal class MetricsProtocolConnectionDecorator : IProtocolConnection
{
    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    private readonly IProtocolConnection _decoratee;
    private volatile Task? _shutdownTask;

    public async Task<(TransportConnectionInformation ConnectionInformation, Task ShutdownRequested)> ConnectAsync(
        CancellationToken cancellationToken)
    {
        Metrics.ClientMetrics.ConnectStart();
        try
        {
            (TransportConnectionInformation connectionInformation, Task shutdownRequested) =
                await _decoratee.ConnectAsync(cancellationToken).ConfigureAwait(false);
            Metrics.ClientMetrics.ConnectSuccess();
            return (connectionInformation, shutdownRequested);
        }
        finally
        {
            Metrics.ClientMetrics.ConnectStop();
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
                // observe and ignore any exception
            }
        }
        Metrics.ClientMetrics.ConnectionStop();
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
        catch
        {
            Metrics.ClientMetrics.ConnectionFailure();
            throw;
        }

        static async Task PerformShutdownAsync(Task decorateeShutdownTask)
        {
            try
            {
                await decorateeShutdownTask.ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Thrown if ConnectAsync wasn't called, or if ShutdownAsync is called twice.
                throw;
            }
            catch
            {
                Metrics.ClientMetrics.ConnectionFailure();
                throw;
            }
        }
    }

    internal MetricsProtocolConnectionDecorator(IProtocolConnection decoratee)
    {
        Metrics.ClientMetrics.ConnectionStart();
        _decoratee = decoratee;
    }
}
