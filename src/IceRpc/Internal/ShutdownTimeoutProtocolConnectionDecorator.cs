// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Adds a shutdown timeout to <see cref="IProtocolConnection" />.</summary>
internal class ShutdownTimeoutProtocolConnectionDecorator : IProtocolConnection
{
    public Task<Exception?> Closed => _decoratee.Closed;

    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    public Task ShutdownRequested => _decoratee.ShutdownRequested;

    private readonly IProtocolConnection _decoratee;
    private readonly TimeSpan _shutdownTimeout;

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
        _decoratee.ConnectAsync(cancellationToken);

    public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
        _decoratee.InvokeAsync(request, cancellationToken);

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        using var cts = new CancellationTokenSource(_shutdownTimeout);
        using CancellationTokenRegistration tokenRegistration =
            cancellationToken.UnsafeRegister(cts => ((CancellationTokenSource)cts!).Cancel(), cts);

        try
        {
            await _decoratee.ShutdownAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"The connection shutdown timed out after {_shutdownTimeout.TotalSeconds} s.");
        }
    }

    internal ShutdownTimeoutProtocolConnectionDecorator(IProtocolConnection decoratee, TimeSpan shutdownTimeout)
    {
        _decoratee = decoratee;
        _shutdownTimeout = shutdownTimeout;
    }
}
