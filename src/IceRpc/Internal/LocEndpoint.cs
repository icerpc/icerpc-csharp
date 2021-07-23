// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc.Internal
{
    /// <summary>Represents an endpoint for the <see cref="TransportCode.Loc"/> pseudo transport. It needs to be converted
    /// into one or more usable endpoints by an interceptor such as
    /// <see cref="Interceptors.Locator(Interop.ILocatorPrx)"/>.</summary>
    internal sealed class LocEndpoint : Endpoint
    {
        // There is no Equals as it's identical to the base.

        protected internal override void EncodeOptions11(IceEncoder encoder) =>
            Debug.Assert(false); // loc endpoints are not marshaled as endpoint with ice1/1.1

        internal static LocEndpoint Create(string location, Protocol protocol) =>
            new(new EndpointData(protocol, "loc", TransportCode.Loc, location, port: 0, ImmutableList<string>.Empty), protocol);

        // Constructor
        internal LocEndpoint(EndpointData data, Protocol protocol)
            : base(data, protocol)
        {
        }
    }

    internal class LocEndpointFactory : IIce1EndpointFactory, IIce2EndpointFactory
    {
        public ushort DefaultUriPort => 0;

        public string Name => "loc";

        public TransportCode TransportCode => TransportCode.Loc;

        public Endpoint CreateEndpoint(EndpointData data, Protocol protocol) =>
            // Drop all options we don't understand
            new LocEndpoint(new EndpointData(protocol, "loc", data.TransportCode, data.Host, data.Port, ImmutableList<string>.Empty),
                            protocol);

        public Endpoint CreateIce1Endpoint(IceDecoder decoder) =>
            throw new InvalidOperationException("an ice1 loc endpoint cannot be read like a regular endpoint");

        public Endpoint CreateIce1Endpoint(Dictionary<string, string?> options, string endpointString)
        {
            (string host, ushort port) = Ice1Parser.ParseHostAndPort(options, endpointString);
            return new LocEndpoint(new EndpointData(Protocol.Ice1, "loc", TransportCode.Loc, host, port, ImmutableList<string>.Empty),
                                   Protocol.Ice1);
        }

        public Endpoint CreateIce2Endpoint(string host, ushort port, Dictionary<string, string> options) =>
            new LocEndpoint(new EndpointData(Protocol.Ice2, "loc", TransportCode.Loc, host, port, ImmutableList<string>.Empty),
                            Protocol.Ice2);
    }
}
