// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;

namespace IceRpc.Transports
{
    /// <summary>Holds various factory delegates and other properties of a transport.</summary>
    public sealed class TransportDescriptor
    {
        /// <summary>The default port for URI endpoints that don't specify a port explicitly.</summary>
        public ushort DefaultUriPort { get; init; }

        /// <summary>Creates an endpoint from an endpoint data and protocol.</summary>
        public Func<EndpointData, Protocol, Endpoint> EndpointFactory { get; }

        /// <summary>Reads an ice1 endpoint from an input stream.</summary>
        public InputStreamReader<Endpoint>? Ice1EndpointFactory { get; init; }

        /// <summary>Creates an ice1 endpoint from an options dictionary and endpoint string.</summary>
        public Func<Dictionary<string, string?>, string, Endpoint>? Ice1EndpointParser { get; init; }

        /// <summary>Creates an ice2 endpoint from a host, port and options dictionary.</summary>
        public Func<string, ushort, Dictionary<string, string>, Endpoint>? Ice2EndpointParser { get; init; }

        /// <summary>The name of this transport in lower case, for example "tcp".</summary>
        public string Name { get; }

        /// <summary>Constructs a transport descriptor.</summary>
        public TransportDescriptor(string name, Func<EndpointData, Protocol, Endpoint> endpointFactory)
        {
            Name = name;
            EndpointFactory = endpointFactory;
        }
    }
}
