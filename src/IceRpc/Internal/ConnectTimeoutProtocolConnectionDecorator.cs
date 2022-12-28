// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Adds a connect timeout to <see cref="IProtocolConnection" />.</summary>
internal class ConnectTimeoutProtocolConnectionDecorator : IProtocolConnection
{
    public Task<Exception?> Closed => _decoratee.Closed;

    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    public Task ShutdownRequested => _decoratee.ShutdownRequested;

    private readonly TimeSpan _connectTimeout;
    private readonly IProtocolConnection _decoratee;

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        using var cts = new CancellationTokenSource(_connectTimeout);
        using CancellationTokenRegistration tokenRegistration =
            cancellationToken.UnsafeRegister(cts => ((CancellationTokenSource)cts!).Cancel(), cts);

        try
        {
            return await _decoratee.ConnectAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"The connection establishment timed out after {_connectTimeout.TotalSeconds} s.");
        }
    }

    public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
        _decoratee.InvokeAsync(request, cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken) => _decoratee.ShutdownAsync(cancellationToken);

    internal ConnectTimeoutProtocolConnectionDecorator(IProtocolConnection decoratee, TimeSpan connectTimeout)
    {
        _decoratee = decoratee;
        _connectTimeout = connectTimeout;
    }
}
