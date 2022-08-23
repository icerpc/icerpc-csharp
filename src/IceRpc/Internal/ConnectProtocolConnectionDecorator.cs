// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Provides a decorator for <see cref="IProtocolConnection"/> that ensures <see cref="IInvoker.InvokeAsync"/>
/// calls <see cref="IProtocolConnection.ConnectAsync"/> when the connection is not connected yet. This decorator
/// also allows multiple and concurrent calls to <see cref="IProtocolConnection.ConnectAsync"/>.</summary>
/// <seealso cref="ClientProtocolConnectionFactory.CreateConnection"/>
internal class ConnectProtocolConnectionDecorator : IProtocolConnection
{
    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    private Task<TransportConnectionInformation>? _connectTask;

    private readonly IProtocolConnection _decoratee;

    // Set to true once the connection is successfully connected. It's not volatile or protected by mutex: in the
    // unlikely event the caller sees false after the connection is connected, it will call ConnectAsync and succeed
    // immediately.
    private bool _isConnected;

    private readonly object _mutex = new();

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel)
    {
        lock (_mutex)
        {
            if (_connectTask is null)
            {
                _connectTask = PerformConnectAsync();
                return _connectTask;
            }
            else
            {
                return PerformWaitForConnectAsync();
            }
        }

        async Task<TransportConnectionInformation> PerformConnectAsync()
        {
            await Task.Yield(); // exit mutex lock

            TransportConnectionInformation connectionInformation = await _decoratee.ConnectAsync(cancel)
                .ConfigureAwait(false);
            _isConnected = true;
            return connectionInformation;
        }

        async Task<TransportConnectionInformation> PerformWaitForConnectAsync()
        {
            await Task.Yield(); // exit mutex lock

            try
            {
                return await _connectTask.WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken != cancel)
            {
                // OCE from _connectTask
                throw new ConnectionAbortedException("connection establishment canceled");
            }
        }
    }

    public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default)
    {
        return _isConnected ? _decoratee.InvokeAsync(request, cancel) : PerformConnectInvokeAsync();

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
