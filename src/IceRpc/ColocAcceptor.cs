// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using ColocChannelReader = System.Threading.Channels.ChannelReader<(long StreamId, object? Frame, bool Fin)>;
using ColocChannelWriter = System.Threading.Channels.ChannelWriter<(long StreamId, object? Frame, bool Fin)>;

namespace IceRpc
{
    /// <summary>The IAcceptor implementation for the colocated transport.</summary>
    internal class ColocAcceptor : IAcceptor
    {
        public Endpoint Endpoint => _endpoint;

        /// <summary>A dictionary that keeps track of all coloc acceptors.</summary>
        private static readonly IDictionary<ColocEndpoint, ColocAcceptor> _colocAcceptorDictionary =
            new ConcurrentDictionary<ColocEndpoint, ColocAcceptor>();

        private readonly Channel<(long, ColocChannelWriter, ColocChannelReader)> _channel;

        private readonly ColocEndpoint _endpoint;

        // TODO: descriptive comment
        private long _nextId;
        private readonly Server _server;

        public async ValueTask<Connection> AcceptAsync()
        {
            (long id, ColocChannelWriter writer, ColocChannelReader reader) =
                await _channel.Reader.ReadAsync().ConfigureAwait(false);

            // For the server-side connection we pass the stream max count from the client since unlike Slic there's
            // no transport initialization to negotiate this configuration and the server-side must limit the number
            // of streams based on the stream max count from the client-side.
            return new Connection(
                _endpoint,
                new ColocSocket(
                    _endpoint,
                    id,
                    writer,
                    reader,
                    _server.ConnectionOptions,
                    _server.Logger),
                _server.ConnectionOptions,
                _server);
        }

        public void Dispose()
        {
            _channel.Writer.Complete();
            _colocAcceptorDictionary.Remove(_endpoint);
        }

        public override string ToString() => _server.ToString();

        internal static bool TryGetValue(
            ColocEndpoint endpoint,
            [NotNullWhen(returnValue: true)] out ColocAcceptor? acceptor) =>
            _colocAcceptorDictionary.TryGetValue(endpoint, out acceptor);

        internal ColocAcceptor(ColocEndpoint endpoint, Server server)
        {
            _endpoint = endpoint;
            _server = server;

            // There's always a single reader (the acceptor) but there might be several writers calling Write
            // concurrently if there are connection establishment attempts from multiple threads.
            _channel = Channel.CreateUnbounded<(long, ColocChannelWriter, ColocChannelReader)>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = true
                });

            if (!_colocAcceptorDictionary.TryAdd(_endpoint, this))
            {
                throw new TransportException($"endpoint '{endpoint}' is already in use");
            }
        }

        internal (ColocChannelReader, ColocChannelWriter, long) NewClientConnection()
        {
            var reader = Channel.CreateUnbounded<(long, object?, bool)>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

            var writer = Channel.CreateUnbounded<(long, object?, bool)>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

            long id = Interlocked.Increment(ref _nextId);

            if (!_channel.Writer.TryWrite((id, writer.Writer, reader.Reader)))
            {
                throw new ConnectionRefusedException();
            }

            return (writer.Reader, reader.Writer, id);
        }
    }
}
