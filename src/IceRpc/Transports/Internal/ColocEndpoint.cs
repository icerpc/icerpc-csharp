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
    internal class ColocEndpoint : Endpoint
    {
        /// <inherit-doc/>
        public override bool? IsSecure => true;

        /// <inherit-doc/>
        public override TransportDescriptor TransportDescriptor => ColocTransportDescriptor;

        internal static TransportDescriptor ColocTransportDescriptor { get; } =
            new(Transport.Coloc, "coloc", CreateEndpoint)
            {
                ListenerFactory = (endpoint, options, logger) =>
                    new ColocListener((ColocEndpoint)endpoint, options, logger),
                DefaultUriPort = 4062,
                Ice1EndpointParser = ParseIce1Endpoint,
                Ice2EndpointParser = (host, port, _) => new ColocEndpoint(host, port, Protocol.Ice2),
                ClientConnectionFactory = CreateClientConnection
            };

        public override bool Equals(Endpoint? other) =>
            other is ColocEndpoint colocEndpoint && base.Equals(colocEndpoint);

        protected internal override void WriteOptions11(OutputStream ostr) =>
            throw new NotSupportedException("colocated endpoint can't be marshaled");

        internal ColocEndpoint(string host, ushort port, Protocol protocol)
            : base(new EndpointData(Transport.Coloc, host, port, ImmutableList<string>.Empty), protocol)
        {
        }

        private static ColocEndpoint CreateEndpoint(EndpointData _, Protocol protocol) =>
            throw new InvalidDataException($"received {protocol.GetName()} endpoint for coloc transport");

        private static MultiStreamConnection CreateClientConnection(
            Endpoint endpoint,
            ClientConnectionOptions options,
            ILogger logger)
        {
            if (endpoint is ColocEndpoint colocEndpoint)
            {
                if (ColocListener.TryGetValue(colocEndpoint, out ColocListener? listener))
                {
                    (ColocChannelReader reader, ColocChannelWriter writer, long id) = listener.NewClientConnection();
                    return new ColocConnection(colocEndpoint, id, writer, reader, options, logger);
                }
                else
                {
                    throw new ConnectionRefusedException();
                }
            }
            else
            {
                throw new ArgumentException("endpoint is not a ColocEndpoint", nameof(endpoint));
            }
        }

        private static ColocEndpoint ParseIce1Endpoint(Dictionary<string, string?> options, string endpointString)
        {
            (string host, ushort port) = ParseHostAndPort(options, endpointString);
            return new(host, port, Protocol.Ice1);
        }
    }
}
