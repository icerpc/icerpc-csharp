// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

using ColocChannelReader = System.Threading.Channels.ChannelReader<(long StreamId, object Frame, bool Fin)>;
using ColocChannelWriter = System.Threading.Channels.ChannelWriter<(long StreamId, object Frame, bool Fin)>;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Endpoint class for the colocated transport.</summary>
    internal class ColocEndpoint : Endpoint, IClientConnectionFactory, IListenerFactory
    {
        public override ushort DefaultPort => Protocol == Protocol.Ice1 ? (ushort)0 : DefaultUriPort;

        public override bool? IsSecure => true;

        internal static IEndpointFactory EndpointFactory { get; } = new ColocEndpointFactory();

        private const ushort DefaultUriPort = 4062;

        public MultiStreamConnection CreateClientConnection(ClientConnectionOptions options, ILogger logger)
        {
            if (ColocListener.TryGetValue(this, out ColocListener? listener))
            {
                (ColocChannelReader reader, ColocChannelWriter writer, long id) = listener.NewClientConnection();
                return new ColocConnection(this, id, writer, reader, options, logger);
            }
            else
            {
                throw new ConnectionRefusedException();
            }
        }

        public IListener CreateListener(ServerConnectionOptions options, ILogger logger) =>
            new ColocListener(this, options, logger);

        public override bool Equals(Endpoint? other) =>
            other is ColocEndpoint colocEndpoint && base.Equals(colocEndpoint);

        protected internal override void WriteOptions11(OutputStream ostr) =>
            throw new NotSupportedException("colocated endpoint can't be marshaled");

        internal ColocEndpoint(string host, ushort port, Protocol protocol)
            : base(new EndpointData(Transport.Coloc, host, port, ImmutableList<string>.Empty), protocol)
        {
        }

        private class ColocEndpointFactory : IIce1EndpointFactory, IIce2EndpointFactory
        {
            public ushort DefaultUriPort => ColocEndpoint.DefaultUriPort;

            public string Name => "coloc";

            public Transport Transport => Transport.Coloc;

            public Endpoint CreateEndpoint(EndpointData _, Protocol protocol) =>
                throw new InvalidDataException($"received {protocol.GetName()} endpoint for coloc transport");

            public Endpoint CreateIce1Endpoint(InputStream _) =>
                throw new InvalidDataException($"received ice1 endpoint for coloc transport");

            public Endpoint CreateIce1Endpoint(Dictionary<string, string?> options, string endpointString)
            {
                (string host, ushort port) = ParseHostAndPort(options, endpointString);
                return new ColocEndpoint(host, port, Protocol.Ice1);
            }

            public Endpoint CreateIce2Endpoint(string host, ushort port, Dictionary<string, string> _) =>
                new ColocEndpoint(host, port, Protocol.Ice2);
        }
    }
}
