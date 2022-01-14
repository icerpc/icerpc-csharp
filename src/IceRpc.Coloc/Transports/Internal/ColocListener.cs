// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the colocated transport.</summary>
    internal class ColocListener : IListener<ISimpleNetworkConnection>
    {
        public Endpoint Endpoint { get; }

        private readonly AsyncQueue<(PipeReader, PipeWriter)> _queue = new();

        public async Task<ISimpleNetworkConnection> AcceptAsync()
        {
            (PipeReader reader, PipeWriter writer) = await _queue.DequeueAsync(default).ConfigureAwait(false);
            return new ColocNetworkConnection(Endpoint, isServer: true, reader, writer);
        }

        public override string ToString() => $"{base.ToString()} {Endpoint}";

        public ValueTask DisposeAsync()
        {
            _queue.TryComplete(new ObjectDisposedException(nameof(ColocListener)));
            return default;
        }

        internal ColocListener(Endpoint endpoint)
        {
            Endpoint = endpoint.WithTransport(ColocTransport.Name);
            if (Endpoint.Params.Count > 1)
            {
                throw new ArgumentException("unknown endpoint parameter", nameof(endpoint));
            }
        }

        internal (PipeReader, PipeWriter) NewClientConnection()
        {
            // By default, the Pipe will pause writes on the PipeWriter when written data is more than 64KB. We could
            // eventually increase this size by providing a PipeOptions instance to the Pipe construction.
            var localPipe = new Pipe();
            var remotePipe = new Pipe();
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
}
