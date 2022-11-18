// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.CodeAnalysis;
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
    private readonly AsyncQueue<(TaskCompletionSource<PipeReader>, PipeReader, CancellationToken)> _queue;

    public async Task<(IDuplexConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);
        try
        {
            while (true)
            {
                (TaskCompletionSource<PipeReader> tcs, PipeReader clientPipeReader, CancellationToken clientConnectCancellationToken) =
                    await _queue.DequeueAsync(cts.Token).ConfigureAwait(false);
                if (clientConnectCancellationToken.IsCancellationRequested)
                {
                    // The client connection establishment was canceled.
                    continue;
                }

                var serverPipe = new Pipe(_pipeOptions);
                var serverConnection = new ServerColocConnection(ServerAddress, serverPipe.Writer, clientPipeReader);
                tcs.SetResult(serverPipe.Reader);
                return (serverConnection, _networkAddress);
            }
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new ObjectDisposedException($"{typeof(ColocListener)}");
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposeCts.IsCancellationRequested)
        {
            // Dispose already called.
            return default;
        }

        // Cancel pending AcceptAsync.
        _disposeCts.Cancel();

        // Complete all the queued connection establishment requests with a null pipe reader. The client connection
        // establishment request will fail with TransportException(TransportErrorCode.ConnectionRefused)
        while (true)
        {
            ValueTask<(TaskCompletionSource<PipeReader>, PipeReader, CancellationToken)> serverConnectTask =
                    _queue.DequeueAsync(default);
            if (serverConnectTask.IsCompletedSuccessfully)
            {
                (TaskCompletionSource<PipeReader> tcs, PipeReader _, CancellationToken _) = serverConnectTask.Result;
                tcs.SetException(new TransportException(TransportErrorCode.ConnectionReset));
            }
            else
            {
                break; // No more queued connection establishment requests.
            }
        }

        // Prevent new connection establishment requests.
        _queue.TryComplete(new ObjectDisposedException($"{typeof(ColocListener)}"));

        _disposeCts.Dispose();

        return default;
    }

    internal ColocListener(ServerAddress serverAddress, int listenBacklog, DuplexConnectionOptions options)
    {
        ServerAddress = serverAddress;

        _queue = new(listenBacklog);
        _networkAddress = new ColocEndPoint(serverAddress);
        _pipeOptions = new PipeOptions(pool: options.Pool, minimumSegmentSize: options.MinSegmentSize);
    }

    internal bool TryQueueConnectAsync(
        PipeReader clientPipeReader,
        CancellationToken cancellationToken,
        [MaybeNullWhen(false)] out Task<PipeReader> serverPipeReaderTask)
    {
        if (_disposeCts.IsCancellationRequested)
        {
            throw new TransportException(TransportErrorCode.ConnectionDisposed);
        }

        var tcs = new TaskCompletionSource<PipeReader>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_queue.Enqueue((tcs, clientPipeReader, cancellationToken)))
        {
            serverPipeReaderTask = tcs.Task;
            return true;
        }
        else
        {
            serverPipeReaderTask = null;
            return false;
        }
    }
}
