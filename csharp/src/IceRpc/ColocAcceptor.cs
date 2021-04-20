// Copyright (c) ZeroC, Inc. All rights reserved.

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

        private readonly ColocEndpoint _endpoint;
        private readonly Server _server;
        private readonly ChannelReader<(long, ColocChannelWriter, ColocChannelReader)> _reader;
        private readonly ChannelWriter<(long, ColocChannelWriter, ColocChannelReader)> _writer;

        public async ValueTask<Connection> AcceptAsync()
        {
            (long id, ColocChannelWriter writer, ColocChannelReader reader) =
                await _reader.ReadAsync().ConfigureAwait(false);

            // For the server-side connection we pass the stream max count from the client since unlike Slic there's
            // no transport initialization to negotiate this configuration and the server-side must limit the number
            // of streams based on the stream max count from the client-side.
            return new ColocConnection(
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

        public void Dispose() => _writer.Complete();

        public override string ToString() => _server.ToString();

        internal ColocAcceptor(
            ColocEndpoint endpoint,
            Server server,
            ChannelWriter<(long, ColocChannelWriter, ColocChannelReader)> writer,
            ChannelReader<(long, ColocChannelWriter, ColocChannelReader)> reader)
        {
            _endpoint = endpoint;
            _server = server;
            _writer = writer;
            _reader = reader;
        }
    }
}
