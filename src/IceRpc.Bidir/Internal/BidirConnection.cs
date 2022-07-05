// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc.Internal;

internal class BidirConnection : IConnection
{
    public bool IsResumable => true;

    public NetworkConnectionInformation? NetworkConnectionInformation => _decoratee?.NetworkConnectionInformation;

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

    internal void ConfigureDecoratee(IConnection connection)
    {
        connection.OnAbort(ex =>
        {
            lock (_mutex)
            {
                if (connection == _decoratee)
                {
                    _decoratee = null;
                    if (_connectionUpdatedSource == null)
                    {
                        _connectionUpdatedSource = new TaskCompletionSource<IConnection>();
                        _ = WaitForReconnectAsync(_connectionUpdatedSource, ex);
                    }
                }
                // else ignore the decoratee has been updated
            }
        });

        connection.OnShutdown(message =>
        {
            Action<string>? onShutdown = null;
            lock (_mutex)
            {
                if (connection == _decoratee)
                {
                    onShutdown = _onShutdown;
                }
                // else ignore the decoratee has been updated
            }
            onShutdown?.Invoke(message);
        });

        async Task WaitForReconnectAsync(TaskCompletionSource<IConnection> connectionUpdatedSource, Exception ex)
        {
            // Wait for the connection update source to be completed using the reconnect timeout, if the connection
            // isn't updated within this timeout period we cancel the updated and call the on abort callback to get
            // rid of the connection.
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.CancelAfter(_reconnectTimeout);
            try
            {
                await connectionUpdatedSource.Task.WaitAsync(cancellationSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Action<Exception>? onAbort = null;
                lock (_mutex)
                {
                    if (_decoratee == null)
                    {
                        // The update task never fails, is either canceled or completed successfully.
                        Debug.Assert(!connectionUpdatedSource.Task.IsCompleted);
                        connectionUpdatedSource.TrySetCanceled();
                        onAbort = _onAbort;
                    }
                }
                onAbort?.Invoke(ex);
            }
        }
    }

    internal void UpdateDecoratee(IConnection connection)
    {
        lock (_mutex)
        {
            if (connection != _decoratee && !(_connectionUpdatedSource?.Task.IsCanceled ?? false))
            {
                _decoratee = connection;

                ConfigureDecoratee(connection);

                if (_connectionUpdatedSource != null)
                {
                    _connectionUpdatedSource.TrySetResult(connection);
                    _connectionUpdatedSource = null;
                }
            }
        }
    }
}
