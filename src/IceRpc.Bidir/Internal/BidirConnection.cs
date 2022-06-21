// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace IceRpc.Internal;

internal class BidirConnection : IConnection
{
    public bool IsResumable => true;

    public NetworkConnectionInformation? NetworkConnectionInformation => Decoratee.NetworkConnectionInformation;

    public Protocol Protocol => Decoratee.Protocol;

    internal IConnection Decoratee
    {
        get => _decoratee;
        set
        {
            lock (_mutex)
            {
                if (value != _decoratee)
                {
                    _decoratee = value;
                    if (_connectionUpdatedSource != null)
                    {
                        _connectionUpdatedSource.SetResult();
                        _connectionUpdatedSource = null;
                    }
                }
            }
        }
    }

    private TaskCompletionSource? _connectionUpdatedSource;
    private IConnection _decoratee;
    private readonly object _mutex = new();
    private TimeSpan _waitTimeout;

    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        IConnection connection = Decoratee;
        try
        {
            return await connection.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
        catch (ConnectionClosedException ex)
        {
            Task? updatedTask = null;
            lock (_mutex)
            {
                // If connection stills points to the actual _decoratee we set updatedTask to wait for the
                // _decorate to be updated otherwise we can retry right away with the new _decoratee
                if (connection == _decoratee)
                {
                    _connectionUpdatedSource ??= new TaskCompletionSource();
                    updatedTask = _connectionUpdatedSource.Task;
                }
                else
                {
                    connection = _decoratee;
                }
            }

            if (updatedTask != null)
            {
                try
                {
                    using var timeoutTokenSource = new CancellationTokenSource(_waitTimeout);
                    using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                        cancel,
                        timeoutTokenSource.Token);
                    await updatedTask.WaitAsync(linkedTokenSource.Token).ConfigureAwait(false);
                    Debug.Assert(connection != _decoratee);
                    connection = _decoratee;
                }
                catch (OperationCanceledException)
                {
                    // Give up on waiting for a new connection and throw the original exception.
                    ExceptionDispatchInfo.Throw(ex);
                }
            }

            return await connection.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
    }

    public void OnClose(Action<Exception> callback) => Decoratee.OnClose(callback);

    internal BidirConnection(IConnection decoratee, TimeSpan waitTimeout)
    {
        _decoratee = decoratee;
        _waitTimeout = waitTimeout;
    }

}
