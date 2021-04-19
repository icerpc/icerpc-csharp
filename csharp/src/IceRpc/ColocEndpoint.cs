// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using ColocChannelReader = System.Threading.Channels.ChannelReader<(long StreamId, object? Frame, bool Fin)>;
using ColocChannelWriter = System.Threading.Channels.ChannelWriter<(long StreamId, object? Frame, bool Fin)>;

namespace IceRpc
{
    /// <summary>The Endpoint class for the colocated transport.</summary>
    internal class ColocEndpoint : Endpoint, IDisposable
    {
        public override bool IsAlwaysSecure => true;

        protected internal override bool HasOptions => Protocol == Protocol.Ice1;

        // The default port with ice1 is 0, just like for IP endpoints.
        protected internal override ushort DefaultPort => Protocol == Protocol.Ice1 ? (ushort)0 : DefaultColocPort;

        internal const ushort DefaultColocPort = 4062;

        internal Server Server { get; }

        private readonly Channel<(long, ColocChannelWriter, ColocChannelReader)> _channel;

        public override IAcceptor Acceptor(Server server) =>
            new ColocAcceptor(this, server, _channel.Writer, _channel.Reader);

        public override bool Equals(Endpoint? other) =>
            other is ColocEndpoint colocEndpoint && Server == colocEndpoint.Server;

        // Temporary
        internal static IDictionary<(string Host, ushort Port, Protocol Protocol), ColocEndpoint> ColocEndpointRegistry { get; } =
            new ConcurrentDictionary<(string, ushort, Protocol), ColocEndpoint>();

        protected internal override void WriteOptions11(OutputStream ostr) =>
            throw new NotSupportedException("colocated endpoint can't be marshaled");

        public override Connection CreateDatagramServerConnection(Server server) =>
            throw new InvalidOperationException();

        // Temporary. Disposed by the server that created this coloc endpoint.
        public void Dispose() => ColocEndpointRegistry.Remove((Host, Port, Protocol));

        private long _nextId;

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

        internal static ColocEndpoint ParseIce1Endpoint(
            Transport transport,
            Dictionary<string, string?> options,
            string endpointString)
        {
            Debug.Assert(transport == Transport.Coloc);
            (string host, ushort port) = ParseHostAndPort(options, endpointString);

            // TODO: this is temporary. It should be possible to create a coloc endpoint before starting the coloc
            // server.
            if (ColocEndpointRegistry.TryGetValue((host, port, Protocol.Ice1), out ColocEndpoint? endpoint))
            {
                return endpoint;
            }
            else
            {
                throw new ArgumentException($"cannot find coloc server for '{endpointString}'");
            }
        }

        internal static ColocEndpoint ParseIce2Endpoint(
            Transport transport,
            string host,
            ushort port,
            Dictionary<string, string> _)
        {
            Debug.Assert(transport == Transport.Coloc);

            // TODO: this is temporary. It should be possible to create a coloc endpoint before starting the coloc
            // server.
            if (ColocEndpointRegistry.TryGetValue((host, port, Protocol.Ice2), out ColocEndpoint? endpoint))
            {
                return endpoint;
            }
            else
            {
                throw new ArgumentException($"cannot find coloc server for 'ice+coloc://{host}:{port}'");
            }
        }

        internal ColocEndpoint(Server server, string host, ushort port)
            : base(new EndpointData(Transport.Coloc, host, port, Array.Empty<string>()), server.Protocol)
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

            // Temporary
            ColocEndpointRegistry.Add((host, port, server.Protocol), this);
        }
    }
}
