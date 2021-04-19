// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ColocChannelReader = System.Threading.Channels.ChannelReader<(long StreamId, object? Frame, bool Fin)>;
using ColocChannelWriter = System.Threading.Channels.ChannelWriter<(long StreamId, object? Frame, bool Fin)>;

namespace IceRpc
{
    /// <summary>The Endpoint class for the colocated transport.</summary>
    internal class ColocEndpoint : Endpoint
    {
        public override bool IsAlwaysSecure => true;

        protected internal override bool HasOptions => false;
        protected internal override ushort DefaultPort => 0;

        internal Server Server { get; }

        private readonly Channel<(long, ColocChannelWriter, ColocChannelReader)> _channel;

        public override IAcceptor Acceptor(Server server) =>
            new ColocAcceptor(this, server, _channel.Writer, _channel.Reader);

        public override bool Equals(Endpoint? other) =>
            other is ColocEndpoint colocEndpoint && Server == colocEndpoint.Server;

        protected internal override void WriteOptions11(OutputStream ostr) =>
            throw new NotSupportedException("colocated endpoint can't be marshaled");

        public override Connection CreateDatagramServerConnection(Server server) =>
            throw new InvalidOperationException();

        private long _nextId;

        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator)
        {
        }

        protected internal override Task<Connection> ConnectAsync(
            OutgoingConnectionOptions options,
            ILogger logger,
            CancellationToken cancel)
        {
            var readerOptions = new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            };
            var reader = Channel.CreateUnbounded<(long, object?, bool)>(readerOptions);

            var writerOptions = new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            };
            var writer = Channel.CreateUnbounded<(long, object?, bool)>(writerOptions);

            long id = Interlocked.Increment(ref _nextId);

            if (!_channel.Writer.TryWrite((id, writer.Writer, reader.Reader)))
            {
                throw new ConnectionRefusedException();
            }

            return Task.FromResult<Connection>(new ColocConnection(
                this,
                new ColocSocket(this, id, reader.Writer, writer.Reader, options, logger),
                options,
                server: null));
        }

        protected internal override Endpoint GetPublishedEndpoint(string publishedHost) =>
            throw new NotSupportedException("cannot create published endpoint for colocated endpoint");

        internal ColocEndpoint(Server server)
            : base(new EndpointData(Transport.Coloc, host: server.ToString(), port: 0, Array.Empty<string>()),
                   server.Protocol)
        {
            Server = server;
            // There's always a single reader (the acceptor) but there might be several writers calling Write
            // concurrently if there are connection establishment attempts from multiple threads.
            var options = new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = true
            };
            _channel = Channel.CreateUnbounded<(long, ColocChannelWriter, ColocChannelReader)>(options);
        }
    }
}
