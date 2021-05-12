// Copyright (c) ZeroC, Inc. All rights reserved.

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
        protected internal override ushort DefaultPort => DefaultLocPort;

        protected internal override bool HasConnect => false;

        internal const ushort DefaultLocPort = 0;

        // There is no Equals as it's identical to the base.

        protected internal override void WriteOptions11(OutputStream ostr) =>
            Debug.Assert(false); // loc endpoints are not marshaled as endpoint with ice1/1.1

        // Drop all options we don't understand.
        internal static LocEndpoint Create(EndpointData data, Protocol protocol) =>
            new(new EndpointData(data.Transport, data.Host, data.Port, ImmutableList<string>.Empty), protocol);

        internal static LocEndpoint Create(string location, Protocol protocol) =>
            new(new EndpointData(Transport.Loc, location, port: 0, ImmutableList<string>.Empty), protocol);

        internal static LocEndpoint ParseIce1Endpoint(
            Transport transport,
            Dictionary<string, string?> options,
            string endpointString)
        {
            Debug.Assert(transport == Transport.Loc);
            (string host, ushort port) = ParseHostAndPort(options, endpointString);

            return new(new EndpointData(transport,
                                        host,
                                        port,
                                        ImmutableList<string>.Empty),
                        Protocol.Ice1);
        }

        internal static LocEndpoint ParseIce2Endpoint(
            Transport transport,
            string host,
            ushort port,
            Dictionary<string, string> _)
        {
            Debug.Assert(transport == Transport.Loc);
            return new(new EndpointData(transport, host, port, ImmutableList<string>.Empty), Protocol.Ice2);
        }

        // Constructor
        private LocEndpoint(EndpointData data, Protocol protocol)
            : base(data, protocol)
        {
        }
    }
}
