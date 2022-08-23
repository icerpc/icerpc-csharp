// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Provides a decorator for <see cref="IProtocolConnection"/> that ensures <see cref="IInvoker.InvokeAsync"/>
/// calls <see cref="IProtocolConnection.ConnectAsync(CancellationToken)"/> when the connection is not connected yet
/// and allows multiple calls to <see cref="IProtocolConnection.ConnectAsync(CancellationToken)"/>.</summary>
internal class ConnectProtocolConnectionDecorator : IProtocolConnection
{
    public bool IsConnected => _decoratee.IsConnected;

    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    private readonly IProtocolConnection _decoratee;

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel) =>
        _decoratee.ConnectAsync(cancel);

    public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default)
    {
        return IsConnected ? _decoratee.InvokeAsync(request, cancel) : PerformConnectInvokeAsync();

        async Task<IncomingResponse> PerformConnectInvokeAsync()
        {
            // Perform the connection establishment without a cancellation token. It will timeout if the
            // connect timeout is reached.
            _ = await ConnectAsync(CancellationToken.None).WaitAsync(cancel).ConfigureAwait(false);
            return await InvokeAsync(request, cancel).ConfigureAwait(false);
        }
    }

    public void OnAbort(Action<Exception> callback) => _decoratee.OnAbort(callback);

    public void OnShutdown(Action<string> callback) => _decoratee.OnShutdown(callback);

    public Task ShutdownAsync(string message, CancellationToken cancel = default) =>
        _decoratee.ShutdownAsync(message, cancel);

    internal ConnectProtocolConnectionDecorator(IProtocolConnection decoratee) => _decoratee = decoratee;
}
