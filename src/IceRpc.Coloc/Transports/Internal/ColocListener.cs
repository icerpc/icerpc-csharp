// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;
using System.Net;

namespace IceRpc.Transports.Internal;

/// <summary>The listener implementation for the colocated transport.</summary>
internal class ColocListener : IListener<IDuplexConnection>
{
    public ServerAddress ServerAddress { get; }

    private readonly EndPoint _networkAddress;
    private readonly PipeOptions _pipeOptions;
    private readonly AsyncQueue<(PipeReader, PipeWriter)> _queue = new();

    public async Task<(IDuplexConnection, EndPoint)> AcceptAsync()
    {
        (PipeReader reader, PipeWriter writer) = await _queue.DequeueAsync(default).ConfigureAwait(false);
        return (new ColocConnection(ServerAddress, _ => (reader, writer)), _networkAddress);
    }

    public void Dispose() => _queue.TryComplete(new ObjectDisposedException($"{typeof(ColocListener)}"));

    internal ColocListener(ServerAddress serverAddress, DuplexConnectionOptions options)
    {
        ServerAddress = serverAddress;

        _networkAddress = new ColocEndPoint(serverAddress);
        _pipeOptions = new PipeOptions(pool: options.Pool, minimumSegmentSize: options.MinSegmentSize);
    }

    internal (PipeReader, PipeWriter) NewClientConnection(DuplexConnectionOptions options)
    {
        // By default, the Pipe will pause writes on the PipeWriter when written data is more than 64KB. We could
        // eventually increase this size by providing a PipeOptions instance to the Pipe construction.
        var localPipe = new Pipe(new PipeOptions(pool: options.Pool, minimumSegmentSize: options.MinSegmentSize));
        var remotePipe = new Pipe(_pipeOptions);
        try
        {
            _queue.Enqueue((localPipe.Reader, remotePipe.Writer));
        }
        catch (ObjectDisposedException)
        {
            throw new ConnectionRefusedException();
        }
        return (remotePipe.Reader, localPipe.Writer);
    }
}
