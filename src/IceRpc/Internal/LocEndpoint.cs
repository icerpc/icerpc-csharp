// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc.Internal
{
    /// <summary>Represents an endpoint for the <see cref="Transport.Loc"/> pseudo transport. It needs to be converted
    /// into one or more usable endpoints by an interceptor such as
    /// <see cref="Interceptors.Locator(Interop.ILocatorPrx)"/>.</summary>
    internal sealed class LocEndpoint : Endpoint
    {
        // There is no Equals as it's identical to the base.

        internal static ITransportDescriptor TransportDescriptor { get; } = new LocTransportDescriptor();
        protected internal override void WriteOptions11(OutputStream ostr) =>
            Debug.Assert(false); // loc endpoints are not marshaled as endpoint with ice1/1.1

        internal static LocEndpoint Create(string location, Protocol protocol) =>
            new(new EndpointData(Transport.Loc, location, port: 0, ImmutableList<string>.Empty), protocol);

        // Constructor
        private LocEndpoint(EndpointData data, Protocol protocol)
            : base(data, protocol)
        {
        }

        private class LocTransportDescriptor : IIce1TransportDescriptor, IIce2TransportDescriptor
        {
            public ushort DefaultUriPort => 0;

            public string Name => "loc";

            public Transport Transport => Transport.Loc;

            public Endpoint CreateEndpoint(EndpointData data, Protocol protocol) =>
                // Drop all options we don't understand
                new LocEndpoint(new EndpointData(data.Transport, data.Host, data.Port, ImmutableList<string>.Empty),
                                protocol);

            public Endpoint CreateEndpoint(InputStream istr) =>
                throw new InvalidOperationException("an ice1 loc endpoint cannot be read like a regular endpoint");

            public Endpoint CreateEndpoint(Dictionary<string, string?> options, string endpointString)
            {
                (string host, ushort port) = Endpoint.ParseHostAndPort(options, endpointString);
                return new LocEndpoint(new EndpointData(Transport.Loc, host, port, ImmutableList<string>.Empty),
                                       Protocol.Ice1);
            }

            public Endpoint CreateEndpoint(string host, ushort port, Dictionary<string, string> options) =>
                new LocEndpoint(new EndpointData(Transport.Loc, host, port, ImmutableList<string>.Empty),
                                Protocol.Ice2);
        }
    }
}
