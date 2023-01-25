// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Provides a log decorator for protocol connections.</summary>
internal class MetricsProtocolConnectionDecorator : IProtocolConnection
{
    public Task<Exception?> Closed => _decoratee.Closed;

    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    public Task ShutdownRequested => _decoratee.ShutdownRequested;

    private readonly IProtocolConnection _decoratee;
    private volatile Task? _stopTask;

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        ClientMetrics.Instance.ConnectStart();
        try
        {
            TransportConnectionInformation result = await _decoratee.ConnectAsync(cancellationToken)
                .ConfigureAwait(false);
            ClientMetrics.Instance.ConnectSuccess();

            _stopTask = WaitForClosedAsync();
            return result;
        }
        finally
        {
            ClientMetrics.Instance.ConnectStop();
        }

        async Task WaitForClosedAsync()
        {
            Exception? exception = await Closed.ConfigureAwait(false);
            if (exception is not null)
            {
                ClientMetrics.Instance.ConnectionFailure();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _decoratee.DisposeAsync().ConfigureAwait(false);
        if (_stopTask is Task stopTask)
        {
            await stopTask.ConfigureAwait(false);
        }
        ClientMetrics.Instance.ConnectionStop();
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
        _decoratee.InvokeAsync(request, cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken) => _decoratee.ShutdownAsync(cancellationToken);

    internal MetricsProtocolConnectionDecorator(IProtocolConnection decoratee)
    {
        ClientMetrics.Instance.ConnectionStart();
        _decoratee = decoratee;
    }
}
