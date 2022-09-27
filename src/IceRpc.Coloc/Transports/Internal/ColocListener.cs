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
    private readonly AsyncQueue<(PipeReader, TaskCompletionSource<PipeReader>)> _queue = new();

    public async Task<(IDuplexConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);
        try
        {
            (PipeReader clientPipeReader, TaskCompletionSource<PipeReader> tcs) = await _queue.DequeueAsync(
                cts.Token).ConfigureAwait(false);

            var serverPipe = new Pipe(_pipeOptions);
            tcs.SetResult(serverPipe.Reader);

            return (new ColocConnection(
                    ServerAddress,
                    serverPipe.Writer,
                    () => Task.FromResult(clientPipeReader)),
                _networkAddress);
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

        // Complete all the queued connection establishment requests.
        var exception = new TransportException(TransportErrorCode.ConnectionRefused);
        while (true)
        {
            ValueTask<(PipeReader Reader, TaskCompletionSource<PipeReader> Tcs)> task = _queue.DequeueAsync(default);
            if (task.IsCompletedSuccessfully)
            {
                task.Result.Tcs.SetException(exception);
            }
            else
            {
                break; // No more queued connection establishment requests.
            }
        }

        // Prevent new connection establishment requests.
        _queue.TryComplete(exception);

        _disposeCts.Dispose();
    }

    internal ColocListener(ServerAddress serverAddress, DuplexConnectionOptions options)
    {
        ServerAddress = serverAddress;

        _networkAddress = new ColocEndPoint(serverAddress);
        _pipeOptions = new PipeOptions(pool: options.Pool, minimumSegmentSize: options.MinSegmentSize);
    }

    internal Task<PipeReader> QueueReaderAsync(PipeReader localReader)
    {
        var tcs = new TaskCompletionSource<PipeReader>();
        if (!_queue.Enqueue((localReader, tcs)) || _disposeCts.IsCancellationRequested)
        {
            throw new TransportException(TransportErrorCode.ConnectionRefused);
        }
        return tcs.Task;
    }
}
