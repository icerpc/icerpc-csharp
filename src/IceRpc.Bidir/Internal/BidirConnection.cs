// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace IceRpc.Internal;

internal class BidirConnection : IConnection
{
    public bool IsResumable => true;

    public NetworkConnectionInformation? NetworkConnectionInformation => _decoratee.NetworkConnectionInformation;

    public Protocol Protocol => _decoratee.Protocol;

    private TaskCompletionSource<IConnection>? _connectionUpdatedSource;
    private IConnection _decoratee;
    private readonly object _mutex = new();
    private readonly TimeSpan _reconnectTimeout;

    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        IConnection connection;
        lock (_mutex)
        {
            connection = _decoratee;
        }

        IncomingResponse response;
        try
        {
            response = await connection.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
        catch (ConnectionClosedException ex)
        {
            Task<IConnection>? updateTask = null;
            lock (_mutex)
            {
                // If connection stills points to the actual _decoratee we set updatedTask to wait for the
                // _decorate to be updated otherwise we can retry right away with the new _decoratee
                if (connection == _decoratee)
                {
                    _connectionUpdatedSource ??= new TaskCompletionSource<IConnection>();
                    updateTask = _connectionUpdatedSource.Task;
                }
                else
                {
                    connection = _decoratee;
                }
            }

            if (updateTask is not null)
            {
                using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                tokenSource.CancelAfter(_reconnectTimeout);
                try
                {
                    connection = await updateTask.WaitAsync(tokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The updateTask is completed by UpdateDecoratee and never completes with an exception,
                    // we give up on waiting for a new connection and throw the original exception.
                    ExceptionDispatchInfo.Throw(ex);
                }
            }

            response = await connection.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
        response.Connection = this;
        return response;
    }

    public void OnClose(Action<Exception> callback)
    {
    }

    internal BidirConnection(IConnection decoratee, TimeSpan reconnectTimeout)
    {
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
                if (_connectionUpdatedSource != null)
                {
                    _connectionUpdatedSource.SetResult(_decoratee);
                    _connectionUpdatedSource = null;
                }
            }
        }
    }
}
