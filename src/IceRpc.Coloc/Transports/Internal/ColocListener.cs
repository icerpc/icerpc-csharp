// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net;
using System.Threading.Channels;

namespace IceRpc.Transports.Internal;

/// <summary>The listener implementation for the colocated transport.</summary>
internal class ColocListener : IListener<IDuplexConnection>
{
    public ServerAddress ServerAddress { get; }

    private readonly CancellationTokenSource _disposeCts = new();
    private readonly EndPoint _networkAddress;
    private readonly PipeOptions _pipeOptions;
    private readonly Channel<(TaskCompletionSource<PipeReader>, PipeReader, CancellationToken)> _channel;

    public async Task<(IDuplexConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);
        try
        {
            while (true)
            {
                (TaskCompletionSource<PipeReader> tcs, PipeReader clientPipeReader, CancellationToken clientConnectCancellationToken) =
                    await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
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
            throw new TransportException(TransportErrorCode.OperationAborted);
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

        // Ensure no more client connection establishment request is queued.
        _channel.Writer.Complete();

        // Complete all the queued client connection establishment requests with TransportErrorCode.ConnectionAborted.
        while (_channel.Reader.TryRead(out (TaskCompletionSource<PipeReader> Tcs, PipeReader, CancellationToken) item))
        {
            item.Tcs.SetException(new TransportException(TransportErrorCode.ConnectionAborted));
        }

        _disposeCts.Dispose();

        return default;
    }

    internal ColocListener(ServerAddress serverAddress, int listenBacklog, DuplexConnectionOptions options)
    {
        ServerAddress = serverAddress;

        _networkAddress = new ColocEndPoint(serverAddress);
        _pipeOptions = new PipeOptions(pool: options.Pool, minimumSegmentSize: options.MinSegmentSize);

        _channel = Channel.CreateBounded<(TaskCompletionSource<PipeReader>, PipeReader, CancellationToken)>(
            new BoundedChannelOptions(listenBacklog)
            {
                SingleReader = true,
                SingleWriter = true
            });
    }

    internal bool TryQueueConnectAsync(
        PipeReader clientPipeReader,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out Task<PipeReader>? serverPipeReaderTask)
    {
        if (_disposeCts.IsCancellationRequested)
        {
            // TODO: Why OperationAborted instead of ConnectionRefused?
            throw new TransportException(TransportErrorCode.OperationAborted);
        }

        var tcs = new TaskCompletionSource<PipeReader>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_channel.Writer.TryWrite((tcs, clientPipeReader, cancellationToken)))
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
