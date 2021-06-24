// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
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

        internal static TransportDescriptor LocTransportDescriptor { get; } =
            new(Transport.Loc, "loc", Create)
            {
                Ice1EndpointParser = ParseIce1Endpoint,
                Ice2EndpointParser = (host, port, _) =>
                    new LocEndpoint(new EndpointData(Transport.Loc, host, port, ImmutableList<string>.Empty),
                                    Protocol.Ice2)
            };
        protected internal override void WriteOptions11(OutputStream ostr) =>
            Debug.Assert(false); // loc endpoints are not marshaled as endpoint with ice1/1.1

        internal static LocEndpoint Create(string location, Protocol protocol) =>
            new(new EndpointData(Transport.Loc, location, port: 0, ImmutableList<string>.Empty), protocol);

        // Drop all options we don't understand.
        private static LocEndpoint Create(EndpointData data, Protocol protocol) =>
            new(new EndpointData(data.Transport, data.Host, data.Port, ImmutableList<string>.Empty), protocol);

        private static LocEndpoint ParseIce1Endpoint(Dictionary<string, string?> options, string endpointString)
        {
            (string host, ushort port) = ParseHostAndPort(options, endpointString);
            return new(new EndpointData(Transport.Loc, host, port, ImmutableList<string>.Empty), Protocol.Ice1);
        }

        // Constructor
        private LocEndpoint(EndpointData data, Protocol protocol)
            : base(data, protocol)
        {
        }
    }
}
