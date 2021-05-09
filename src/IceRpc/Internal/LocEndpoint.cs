// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
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
        protected internal override bool HasOptions => Protocol == Protocol.Ice1 || Data.Options.Length > 0;

        internal const ushort DefaultLocPort = 0;

        // There is no Equals as it's identical to the base.

        protected internal override void WriteOptions11(OutputStream ostr) =>
            Debug.Assert(false); // loc endpoints are not marshaled as endpoint with ice1/1.1

        internal static LocEndpoint Create(EndpointData data, Protocol protocol)
        {
            // Drop all options we don't understand.

            if (protocol == Protocol.Ice1 && data.Options.Length > 1)
            {
                // Well-known proxy.
                data = new EndpointData(data.Transport, data.Host, data.Port, new string[] { data.Options[0] });
            }
            else if (protocol != Protocol.Ice1 && data.Options.Length > 0)
            {
                data = new EndpointData(data.Transport, data.Host, data.Port, Array.Empty<string>());
            }
            return new(data, protocol);
        }
        internal static LocEndpoint Create(string location, Protocol protocol) =>
            new(new EndpointData(Transport.Loc, location, port: 0, Array.Empty<string>()), protocol);

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
                                        Array.Empty<string>()),
                        Protocol.Ice1);
        }

        internal static LocEndpoint ParseIce2Endpoint(
            Transport transport,
            string host,
            ushort port,
            Dictionary<string, string> _)
        {
            Debug.Assert(transport == Transport.Loc);
            return new(new EndpointData(transport, host, port, Array.Empty<string>()), Protocol.Ice2);
        }

        // Constructor
        private LocEndpoint(EndpointData data, Protocol protocol)
            : base(data, protocol)
        {
        }
    }
}
