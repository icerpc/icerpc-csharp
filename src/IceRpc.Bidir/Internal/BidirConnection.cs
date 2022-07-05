// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc.Internal;

internal class BidirConnection : IConnection
{
    public bool IsResumable => true;

    // TODO this was removed in HEAD
    public NetworkConnectionInformation? NetworkConnectionInformation => _decoratee!.NetworkConnectionInformation;

    public Protocol Protocol { get; }

    private TaskCompletionSource<IConnection>? _connectionUpdatedSource;
    private IConnection? _decoratee;
    private readonly object _mutex = new();
    private Action<Exception>? _onAbort;
    private Action<string>? _onShutdown;
    private readonly TimeSpan _reconnectTimeout;

    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        IConnection? connection = null;
        Task<IConnection>? updateTask = null;
        lock (_mutex)
        {
            if (_decoratee == null)
            {
                updateTask = _connectionUpdatedSource!.Task;
            }
            else
            {
                connection = _decoratee;
            }
        }

        if (updateTask != null)
        {
            try
            {
                connection = await updateTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new ConnectionAbortedException();
            }
        }

        Debug.Assert(connection != null);

        IncomingResponse response = await connection.InvokeAsync(request, cancel).ConfigureAwait(false);
        response.Connection = this;
        return response;
    }

    public void OnAbort(Action<Exception> callback) => _onAbort = callback;

    public void OnShutdown(Action<string> callback) => _onShutdown = callback;

    internal BidirConnection(IConnection decoratee, TimeSpan reconnectTimeout)
    {
        Protocol = decoratee.Protocol;
        _decoratee = decoratee;
        _reconnectTimeout = reconnectTimeout;
    }

    internal void UpdateDecoratee(IConnection connection)
    {
        lock (_mutex)
        {
            if (connection != _decoratee)
            {
                _decoratee = connection;
                connection.OnAbort(ex =>
                {
                    _decoratee = null;
                    if (_connectionUpdatedSource == null)
                    {
                        _connectionUpdatedSource = new TaskCompletionSource<IConnection>();
                        _ = WaitForReconnectAsync(_connectionUpdatedSource, ex);
                    }
                });

                connection.OnShutdown(message =>
                {
                    lock (_mutex)
                    {
                        if (connection == _decoratee)
                        {
                            _onShutdown?.Invoke(message);
                        }
                    }
                });

                if (_connectionUpdatedSource != null)
                {
                    _connectionUpdatedSource.TrySetResult(connection);
                    _connectionUpdatedSource = null;
                }
            }
        }

        async Task WaitForReconnectAsync(TaskCompletionSource<IConnection> completionSource, Exception ex)
        {
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.CancelAfter(_reconnectTimeout);
            try
            {
                await completionSource.Task.WaitAsync(cancellationSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The update task never fails, is either canceled or completed successfully.
                completionSource.TrySetCanceled();

                lock (_mutex)
                {
                    if (connection == _decoratee)
                    {
                        _onAbort?.Invoke(ex);
                    }
                }
            }
        }
    }
}
