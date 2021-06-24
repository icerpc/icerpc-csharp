// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IceRpc.Transports
{
    /// <summary>Holds properties for a transport, such as factory functions to create endpoints and connections.
    /// </summary>
    public sealed class TransportDescriptor
    {
        /// <summary>Creates a connection that receives data from one or multiple clients. This factory is used to
        /// implement a transport that can only communicate with a single client (e.g. a serial based transport) or that
        /// can receive data from multiple clients with a single connection (e.g: UDP). Most transports should provide
        /// instead a <see cref="ListenerFactory"/>.</summary>
        public Func<Endpoint, ServerConnectionOptions, ILogger, MultiStreamConnection>? Acceptor
        {
            get; init;
        }

        /// <summary>Creates a connection to a remote endpoint.</summary>
        public Func<Endpoint, ClientConnectionOptions, ILogger, MultiStreamConnection>? Connector
        {
            get; init;
        }

        /// <summary>The default port for URI endpoints that don't specify a port explicitly.</summary>
        public ushort DefaultUriPort { get; init; }

        /// <summary>The endpoint factory. It creates an endpoint from an endpoint data and protocol.</summary>
        public Func<EndpointData, Protocol, Endpoint> EndpointFactory { get; }

        /// <summary>The ice1 endpoint factory. It reads an ice1 endpoint from an input stream.</summary>
        public InputStreamReader<Endpoint>? Ice1EndpointFactory { get; init; }

        /// <summary>The ice1 endpoint parser. It creates an ice1 endpoint from an options dictionary and endpoint
        /// string.</summary>
        public Func<Dictionary<string, string?>, string, Endpoint>? Ice1EndpointParser { get; init; }

        /// <summary>The ice2 endpoint parser. It creates an ice2 endpoint from a host, port and options dictionary.
        /// </summary>
        public Func<string, ushort, Dictionary<string, string>, Endpoint>? Ice2EndpointParser { get; init; }

        /// <summary>Creates a listener that listens on a local endpoint. This listener then accepts connections from
        /// clients.</summary>
        public Func<Endpoint, ServerConnectionOptions, ILogger, IListener>? ListenerFactory { get; init; }

        /// <summary>The name of this transport in lower case, for example "tcp".</summary>
        public string Name { get; }

        /// <summary>The transport enumerator.</summary>
        public Transport Transport { get; }

        /// <summary>Constructs a transport descriptor.</summary>
        public TransportDescriptor(
            Transport transport,
            string name,
            Func<EndpointData, Protocol, Endpoint> endpointFactory)
        {
            EndpointFactory = endpointFactory;
            Name = name;
            Transport = transport;
        }
    }
}
