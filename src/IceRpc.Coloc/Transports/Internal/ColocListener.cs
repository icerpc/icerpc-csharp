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

    // The channel used by the client connection ConnectAsync method to queue a connection establishment request. A
    // client connection establishment request is represented by:
    // - a TaskCompletionSource which is completed by AcceptAsync when the connection is accepted. The server connection
    //   pipe reader is set as the result. ClientColocConnection.ConnectAsync waits on the task completion source task.
    // - the client connection pipe reader provided to the server connection when the server connection is created by
    //   AcceptAsync.
    // - the cancellation token from the caller of ClientColocConnection.ConnectAsync. The client cancellation token is
    //   used by AcceptAsync to check if the connection establishment request has been canceled.
    private readonly Channel<(TaskCompletionSource<PipeReader>, PipeReader, CancellationToken)> _channel;

    public async Task<(IDuplexConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        if (_disposeCts.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(ColocListener));
        }

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
            throw new IceRpcException(IceRpcError.OperationAborted);
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

        // Complete all the queued client connection establishment requests with IceRpcError.ConnectionAborted.
        while (_channel.Reader.TryRead(
            out (TaskCompletionSource<PipeReader> Tcs, PipeReader, CancellationToken CancellationToken) item))
        {
            if (item.CancellationToken.IsCancellationRequested)
            {
                // If the connection establishment has been canceled mark the task as canceled otherwise the
                // exception would end up as an unobserved exception.
                item.Tcs.SetCanceled();
            }
            else
            {
                item.Tcs.SetException(new IceRpcException(IceRpcError.ConnectionAborted));
            }
        }

        _disposeCts.Dispose();

        return default;
    }

    internal ColocListener(
        ServerAddress serverAddress,
        ColocTransportOptions colocTransportOptions,
        DuplexConnectionOptions duplexConnectionOptions)
    {
        ServerAddress = serverAddress;

        _networkAddress = new ColocEndPoint(serverAddress);
        _pipeOptions = new PipeOptions(
            pool: duplexConnectionOptions.Pool,
            minimumSegmentSize: duplexConnectionOptions.MinSegmentSize,
            pauseWriterThreshold: colocTransportOptions.PauseWriterThreshold,
            resumeWriterThreshold: colocTransportOptions.ResumeWriterThreshold);

        _channel = Channel.CreateBounded<(TaskCompletionSource<PipeReader>, PipeReader, CancellationToken)>(
            new BoundedChannelOptions(colocTransportOptions.ListenBacklog)
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
