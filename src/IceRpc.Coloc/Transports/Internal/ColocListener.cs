// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>The listener implementation for the colocated transport.</summary>
internal class ColocListener : IDuplexListener
{
    public Endpoint Endpoint { get; }

    private readonly PipeOptions _pipeOptions;
    private readonly AsyncQueue<(PipeReader, PipeWriter)> _queue = new();

    public async Task<IDuplexConnection> AcceptAsync()
    {
        (PipeReader reader, PipeWriter writer) = await _queue.DequeueAsync(default).ConfigureAwait(false);
        return new ColocDuplexConnection(Endpoint, _ => (reader, writer));
    }

    public void Dispose() => _queue.TryComplete(new ObjectDisposedException(nameof(ColocListener)));

    internal ColocListener(DuplexListenerOptions options)
    {
        Endpoint = options.Endpoint;
        _pipeOptions = new PipeOptions(
            pool: options.ServerConnectionOptions.Pool,
            minimumSegmentSize: options.ServerConnectionOptions.MinimumSegmentSize);
    }

    internal (PipeReader, PipeWriter) NewClientConnection()
    {
        // By default, the Pipe will pause writes on the PipeWriter when written data is more than 64KB. We could
        // eventually increase this size by providing a PipeOptions instance to the Pipe construction.
        var localPipe = new Pipe(_pipeOptions);
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
