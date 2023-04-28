// Copyright (c) ZeroC, Inc.

using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net;
using System.Threading.Channels;

namespace IceRpc.Transports.Coloc.Internal;

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
    private readonly Channel<(TaskCompletionSource<PipeReader>, PipeReader)> _channel;

    public async Task<(IDuplexConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposeCts.IsCancellationRequested, this);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);
        try
        {
            while (true)
            {
                (TaskCompletionSource<PipeReader> tcs, PipeReader clientPipeReader) =
                    await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);

                var serverPipe = new Pipe(_pipeOptions);
                if (tcs.TrySetResult(serverPipe.Reader))
                {
                    var serverConnection = new ServerColocConnection(
                        ServerAddress,
                        serverPipe.Writer,
                        clientPipeReader);
                    return (serverConnection, _networkAddress);
                }
                else
                {
                    // The client connection establishment was canceled.
                    serverPipe.Writer.Complete();
                    serverPipe.Reader.Complete();
                }
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

        // Complete all the queued client connection establishment requests with IceRpcError.ConnectionRefused. Use
        // TrySetException in case the task has been already canceled.
        while (_channel.Reader.TryRead(out (TaskCompletionSource<PipeReader> Tcs, PipeReader) item))
        {
            item.Tcs.TrySetException(new IceRpcException(IceRpcError.ConnectionRefused));
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

        // Create a bounded channel with a capacity that matches the listen backlog, and with
        // the default concurrency settings that allow multiple reader and writers.
        _channel = Channel.CreateBounded<(TaskCompletionSource<PipeReader>, PipeReader)>(
            new BoundedChannelOptions(colocTransportOptions.ListenBacklog));
    }

    /// <summary>Queue client connection establishment requests from the client.</summary>
    /// <param name="clientPipeReader">A <see cref="PipeReader"/> for reading from the client connection.</param>
    /// <param name="cancellationToken">>A cancellation token that receives the cancellation requests.</param>
    /// <param name="serverPipeReaderTask">A task that returns a <see cref="PipeReader"/> for reading from the server
    /// connection.</param>
    /// <returns>Returns true if the connection establishment request has been queue otherwise, false.</returns>
    internal bool TryQueueConnect(
        PipeReader clientPipeReader,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out Task<PipeReader>? serverPipeReaderTask)
    {
        // Create a tcs that is completed by AcceptAsync when accepts the corresponding connection, at which point
        // the client side connect operation will complete.
        // We use RunContinuationsAsynchronously to avoid the ConnectAsync continuation end up running in the AcceptAsync
        // loop that completes this tcs.
        var tcs = new TaskCompletionSource<PipeReader>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_channel.Writer.TryWrite((tcs, clientPipeReader)))
        {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
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
