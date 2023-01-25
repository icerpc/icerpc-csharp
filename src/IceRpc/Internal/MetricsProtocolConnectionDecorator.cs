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
        ClientMetrics.Instance.ConnectStart();
        try
        {
            (TransportConnectionInformation connectionInformation, Task shutdownRequested) =
                await _decoratee.ConnectAsync(cancellationToken).ConfigureAwait(false);
            ClientMetrics.Instance.ConnectSuccess();
            return (connectionInformation, shutdownRequested);
        }
        finally
        {
            ClientMetrics.Instance.ConnectStop();
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
        ClientMetrics.Instance.ConnectionStop();
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
        _decoratee.InvokeAsync(request, cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        // It's not thread-safe because we don't need to be perfectly thread-safe for InvalidOperationException.
        _shutdownTask = _shutdownTask is null ? PerformShutdownAsync() :
            throw new InvalidOperationException("The connection is already shut down.");

        return _shutdownTask;

        async Task PerformShutdownAsync()
        {
            try
            {
                await _decoratee.ShutdownAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                ClientMetrics.Instance.ConnectionFailure();
                throw;
            }
        }
    }

    internal MetricsProtocolConnectionDecorator(IProtocolConnection decoratee)
    {
        ClientMetrics.Instance.ConnectionStart();
        _decoratee = decoratee;
    }
}
