// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IceRpc.Transports
{
    /// <summary>Holds properties for a transport, such as factory functions to create endpoints and connections.
    /// </summary>
    public sealed class TransportDescriptor
    {
        /// <summary>Creates an acceptor. An acceptor listens for connection establishment requests from clients and
        /// creates a new connection for each client. This is typically used to implement a stream-based transport such
        /// as TCP or QUIC. Datagram or serial transports provide instead <see cref="IncomingConnectionFactory"/>.
        /// </summary>
        public Func<Endpoint, IncomingConnectionOptions, ILogger, IAcceptor>? AcceptorFactory { get; init; }

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

        /// <summary>Creates an incoming connection to receive data from one or multiple clients. This is used to
        /// implement a transport which can only communicate with a single client (e.g. a serial
        /// based transport) or which can received data from multiple clients with a single connection (e.g: UDP).
        /// </summary>
        public Func<Endpoint, IncomingConnectionOptions, ILogger, MultiStreamConnection>? IncomingConnectionFactory { get; init; }

        /// <summary>The name of this transport in lower case, for example "tcp".</summary>
        public string Name { get; }

        /// <summary>Creates an outgoing connection.</summary>
        public Func<Endpoint, OutgoingConnectionOptions, ILogger, MultiStreamConnection>? OutgoingConnectionFactory { get; init; }

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
