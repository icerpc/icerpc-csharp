// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Provides a log decorator for protocol connections.</summary>
internal class MetricsProtocolConnectionDecorator : IProtocolConnection
{
    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    public Task ShutdownComplete => _decoratee.ShutdownComplete;

    private readonly IProtocolConnection _decoratee;
    private readonly Task _shutdownAsync;

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        ClientMetrics.Instance.ConnectStart();
        try
        {
            TransportConnectionInformation result = await _decoratee.ConnectAsync(cancellationToken)
                .ConfigureAwait(false);
            ClientMetrics.Instance.ConnectSuccess();
            return result;
        }
        finally
        {
            ClientMetrics.Instance.ConnectStop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _decoratee.DisposeAsync().ConfigureAwait(false);
        await _shutdownAsync.ConfigureAwait(false);
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
        _decoratee.InvokeAsync(request, cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        _decoratee.ShutdownAsync(cancellationToken);

    internal MetricsProtocolConnectionDecorator(IProtocolConnection decoratee)
    {
        ClientMetrics.Instance.ConnectionStart();

        _decoratee = decoratee;
        _shutdownAsync = ShutdownAsync();

        // This task executes exactly once per decorated connection.
        async Task ShutdownAsync()
        {
            try
            {
                await ShutdownComplete.ConfigureAwait(false);
            }
            catch (Exception)
            {
                ClientMetrics.Instance.ConnectionFailure();
            }
            ClientMetrics.Instance.ConnectionStop();
        }
    }
}
