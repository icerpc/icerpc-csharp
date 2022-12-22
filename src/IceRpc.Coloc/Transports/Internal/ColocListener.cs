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
                (TaskCompletionSource<PipeReader> tcs, PipeReader clientPipeReader, CancellationToken _) =
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
                // Else the client connection establishment was canceled.
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
        while (_channel.Reader.TryRead(
            out (TaskCompletionSource<PipeReader> Tcs, PipeReader, CancellationToken CancellationToken) item))
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

        _channel = Channel.CreateBounded<(TaskCompletionSource<PipeReader>, PipeReader, CancellationToken)>(
            new BoundedChannelOptions(colocTransportOptions.ListenBacklog)
            {
                SingleReader = true,
                SingleWriter = true
            });
    }

    /// <summary>Queue client connection establishment requests from the client.</summary>
    /// <param name="clientPipeReader">A <see cref="PipeReader"/> for reading from the client connection.</param>
    /// <param name="cancellationToken">>A cancellation token that receives the cancellation requests.</param>
    /// <param name="serverPipeReaderTask">A <see cref="PipeReader"/> for reading from the server connection.</param>
    /// <returns>Returns true if the connection establishment request has been queue otherwise, false.</returns>
    internal bool TryQueueConnect(
        PipeReader clientPipeReader,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out Task<PipeReader>? serverPipeReaderTask)
    {
        if (_disposeCts.IsCancellationRequested)
        {
            throw new IceRpcException(IceRpcError.ConnectionRefused);
        }

        // Create a tcs that is completed by AcceptAsync when accepts the corresponding connection, at which point
        // the client side connect operation will complete.
        var tcs = new TaskCompletionSource<PipeReader>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_channel.Writer.TryWrite((tcs, clientPipeReader, cancellationToken)))
        {
            cancellationToken.Register(tcs.SetCanceled);
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
