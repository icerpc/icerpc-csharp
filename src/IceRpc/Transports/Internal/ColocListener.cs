// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the colocated transport.</summary>
    internal class ColocListener : IListener<ISimpleNetworkConnection>
    {
        public Endpoint Endpoint { get; }

        /// <summary>A dictionary that keeps track of all coloc listeners.</summary>
        private static readonly IDictionary<Endpoint, ColocListener> _colocListenerDictionary =
            new ConcurrentDictionary<Endpoint, ColocListener>();

        private readonly AsyncQueue<(PipeReader, PipeWriter)> _queue = new();

        public async Task<ISimpleNetworkConnection> AcceptAsync()
        {
            (PipeReader reader, PipeWriter writer) = await _queue.DequeueAsync(default).ConfigureAwait(false);
            return new ColocNetworkConnection(Endpoint, isServer: true, reader, writer);
        }

        public override string ToString() => $"{base.ToString()} {Endpoint}";

        internal static bool TryGetValue(
            Endpoint endpoint,
            [NotNullWhen(returnValue: true)] out ColocListener? listener) =>
            _colocListenerDictionary.TryGetValue(endpoint, out listener);

        public ValueTask DisposeAsync()
        {
            _queue.Complete(new ObjectDisposedException(nameof(ColocListener)));
            _colocListenerDictionary.Remove(Endpoint);
            return default;
        }

        internal ColocListener(Endpoint endpoint)
        {
            if (endpoint.Params.Count > 0)
            {
                throw new FormatException($"unknown parameter '{endpoint.Params[0].Name}' in endpoint '{endpoint}'");
            }
            if (!_colocListenerDictionary.TryAdd(endpoint, this))
            {
                throw new TransportException($"endpoint '{endpoint}' is already in use");
            }
            Endpoint = endpoint;
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
