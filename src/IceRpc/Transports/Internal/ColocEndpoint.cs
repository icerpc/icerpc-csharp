// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

using ColocChannelReader = System.Threading.Channels.ChannelReader<(long StreamId, object Frame, bool Fin)>;
using ColocChannelWriter = System.Threading.Channels.ChannelWriter<(long StreamId, object Frame, bool Fin)>;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Endpoint class for the colocated transport.</summary>
    internal class ColocEndpoint : Endpoint
    {
        public override bool? IsSecure => true;

        protected internal override bool HasAcceptor => true;

        // The default port with ice1 is 0, just like for IP endpoints.
        protected internal override ushort DefaultPort => Protocol == Protocol.Ice1 ? (ushort)0 : DefaultColocPort;

        internal const ushort DefaultColocPort = 4062;

        public override bool Equals(Endpoint? other) =>
            other is ColocEndpoint colocEndpoint && base.Equals(colocEndpoint);

        protected internal override void WriteOptions11(OutputStream ostr) =>
            throw new NotSupportedException("colocated endpoint can't be marshaled");

        protected internal override IAcceptor CreateAcceptor(
            IncomingConnectionOptions options,
            ILogger logger) => new ColocAcceptor(this, options, logger);

        protected internal override MultiStreamConnection CreateClientSocket(
            OutgoingConnectionOptions options,
            ILogger logger)
        {
            if (ColocAcceptor.TryGetValue(this, out ColocAcceptor? acceptor))
            {
                (ColocChannelReader reader, ColocChannelWriter writer, long id) = acceptor.NewClientConnection();
                return new ColocConnection(this, id, writer, reader, options, logger);
            }
            else
            {
                throw new ConnectionRefusedException();
            }
        }

        // Unmarshaling constructor
        internal static ColocEndpoint CreateEndpoint(EndpointData _, Protocol protocol) =>
            throw new InvalidDataException($"received {protocol.GetName()} endpoint for coloc transport");

        internal static ColocEndpoint ParseIce1Endpoint(
            Transport transport,
            Dictionary<string, string?> options,
            string endpointString)
        {
            Debug.Assert(transport == Transport.Coloc);
            (string host, ushort port) = ParseHostAndPort(options, endpointString);
            return new(host, port, Protocol.Ice1);
        }

        internal static ColocEndpoint ParseIce2Endpoint(
            Transport transport,
            string host,
            ushort port,
            Dictionary<string, string> _)
        {
            Debug.Assert(transport == Transport.Coloc);
            return new(host, port, Protocol.Ice2);
        }

        internal ColocEndpoint(string host, ushort port, Protocol protocol)
            : base(new EndpointData(Transport.Coloc, host, port, ImmutableList<string>.Empty), protocol)
        {
        }
    }
}
