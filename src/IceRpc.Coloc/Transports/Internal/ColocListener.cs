// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;
using System.Net;

namespace IceRpc.Transports.Internal;

/// <summary>The listener implementation for the colocated transport.</summary>
internal class ColocListener : IListener<IDuplexConnection>
{
    public ServerAddress ServerAddress { get; }

    private readonly CancellationTokenSource _disposeCts = new();
    private readonly EndPoint _networkAddress;
    private readonly PipeOptions _pipeOptions;
    private readonly AsyncQueue<Func<PipeReader?, PipeReader?>> _queue = new();

    public async Task<(IDuplexConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);
        try
        {
            while (true)
            {
                Func<PipeReader?, PipeReader?> serverConnect = await _queue.DequeueAsync(
                    cts.Token).ConfigureAwait(false);

                // Create the server-side pipe and provide the reader to the client connection establishment request.
                // The connection establishment request return the client-side pipe reader or null if the conneciton
                // connection establishment request was canceled.
                var serverPipe = new Pipe(_pipeOptions);
                PipeReader? clientPipeReader = serverConnect(serverPipe.Reader);
                if (clientPipeReader is null)
                {
                    // The client connection establishment was canceled.
                    serverPipe.Reader.Complete();
                    serverPipe.Writer.Complete();
                }
                else
                {
                    return (new ServerColocConnection(ServerAddress, serverPipe.Writer, clientPipeReader),
                            _networkAddress);
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new ObjectDisposedException($"{typeof(ColocListener)}");
        }
    }

    public void Dispose()
    {
        if (_disposeCts.IsCancellationRequested)
        {
            // Dispose already called.
            return;
        }

        // Cancel pending AcceptAsync.
        _disposeCts.Cancel();

        // Complete all the queued connection establishment requests with a null pipe reader. The client connection
        // establishment request will fail with TransportException(TransportErrorCode.ConnectionRefused)
        while (true)
        {
            ValueTask<Func<PipeReader?, PipeReader?>> serverConnectTask = _queue.DequeueAsync(default);
            if (serverConnectTask.IsCompletedSuccessfully)
            {
                serverConnectTask.Result.Invoke(null);
            }
            else
            {
                break; // No more queued connection establishment requests.
            }
        }

        // Prevent new connection establishment requests.
        _queue.TryComplete(new ObjectDisposedException($"{typeof(ColocListener)}"));

        _disposeCts.Dispose();
    }

    internal ColocListener(ServerAddress serverAddress, DuplexConnectionOptions options)
    {
        ServerAddress = serverAddress;

        _networkAddress = new ColocEndPoint(serverAddress);
        _pipeOptions = new PipeOptions(pool: options.Pool, minimumSegmentSize: options.MinSegmentSize);
    }

    /// <summary>Queue the connection establishment request from the client.</summary>
    internal void QueueConnectAsync(Func<PipeReader?, PipeReader?> connect)
    {
        if (!_queue.Enqueue(connect) || _disposeCts.IsCancellationRequested)
        {
            throw new TransportException(TransportErrorCode.ConnectionRefused);
        }
    }
}
