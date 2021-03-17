// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Channels;
using System.Threading.Tasks;
using ColocatedChannelReader = System.Threading.Channels.ChannelReader<(long StreamId, object? Frame, bool Fin)>;
using ColocatedChannelWriter = System.Threading.Channels.ChannelWriter<(long StreamId, object? Frame, bool Fin)>;

namespace IceRpc
{
    /// <summary>The IAcceptor implementation for the colocated transport.</summary>
    internal class ColocatedAcceptor : IAcceptor
    {
        public Endpoint Endpoint => _endpoint;

        private readonly ColocatedEndpoint _endpoint;
        private readonly Server _server;
        private readonly ChannelReader<(long, ColocatedChannelWriter, ColocatedChannelReader)> _reader;
        private readonly ChannelWriter<(long, ColocatedChannelWriter, ColocatedChannelReader)> _writer;

        public async ValueTask<Connection> AcceptAsync()
        {
            (long id, ColocatedChannelWriter writer, ColocatedChannelReader reader) =
                await _reader.ReadAsync().ConfigureAwait(false);

            return new ColocatedConnection(_endpoint,
                                           new ColocatedSocket(_endpoint, id, writer, reader, true),
                                           label: null,
                                           _server);
        }

        public void Dispose() => _writer.Complete();

        public override string ToString() =>
            _endpoint.Server.Name.Length == 0 ? "unnamed server" : _endpoint.Server.Name;

        internal ColocatedAcceptor(
            ColocatedEndpoint endpoint,
            Server server,
            ChannelWriter<(long, ColocatedChannelWriter, ColocatedChannelReader)> writer,
            ChannelReader<(long, ColocatedChannelWriter, ColocatedChannelReader)> reader)
        {
            _endpoint = endpoint;
            _server = server;
            _writer = writer;
            _reader = reader;
        }
    }
}
