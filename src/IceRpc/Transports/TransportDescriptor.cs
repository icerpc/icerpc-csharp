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
        /// <summary>The client connection factory.</summary>
        /// <seealso cref="ClientNetworkSocketFactory"/>
        public Func<Endpoint, ClientConnectionOptions, ILogger, MultiStreamConnection>? ClientConnectionFactory
        {
            get; init;
        }

        /// <summary>The client network socket factory. Setting this value sets <see cref="ClientConnectionFactory"/>.
        /// Set this value when describing a <see cref="NetworkSocket"/>-based transport.</summary>
        public Func<Endpoint, ITransportOptions?, ILogger, NetworkSocket>? ClientNetworkSocketFactory
        {
            get => _clientNetworkSocketFactory;

            init
            {
                _clientNetworkSocketFactory = value;
                ClientConnectionFactory = _clientNetworkSocketFactory == null ? null :
                (endpoint, options, logger) =>
                {
                    NetworkSocket singleStreamConnection =
                        _clientNetworkSocketFactory(endpoint, options.TransportOptions, logger);

                    return endpoint.Protocol == Protocol.Ice1 ?
                        new Ice1Connection(endpoint, singleStreamConnection, options) :
                        new SlicConnection(endpoint, singleStreamConnection, options);
                };
            }
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

        /// <summary>The listener factory. An listener listens for connection establishment requests from clients and
        /// creates (accepts) a new connection for each client. This is typically used to implement a stream-based
        /// transport such as TCP or QUIC. Datagram or serial transports provide instead
        /// <see cref="ServerConnectionFactory"/>.</summary>
        public Func<Endpoint, ServerConnectionOptions, ILogger, IListener>? ListenerFactory { get; init; }

        /// <summary>The name of this transport in lower case, for example "tcp".</summary>
        public string Name { get; }

        /// <summary>The server connection factory. It creates server connections that receive data from one or
        /// multiple clients. This factory is used to implement a transport that can only communicate with a single
        /// client (e.g. a serial based transport) or that can receive data from multiple clients with a single
        /// connection (e.g: UDP).</summary>
        /// <seealso cref="ServerNetworkSocketFactory"/>
        public Func<Endpoint, ServerConnectionOptions, ILogger, MultiStreamConnection>? ServerConnectionFactory
        {
            get; init;
        }

        /// <summary>The server network socket factory. Setting this value sets <see cref="ServerConnectionFactory"/>.
        /// Set this value when describing a <see cref="NetworkSocket"/>-based transport that does not provide a
        /// listener.</summary>
        public Func<Endpoint, ITransportOptions?, ILogger, (NetworkSocket, Endpoint)>? ServerNetworkSocketFactory
        {
            get => _serverNetworkSocketFactory;

            init
            {
                _serverNetworkSocketFactory = value;
                ServerConnectionFactory = _serverNetworkSocketFactory == null ? null :
                (endpoint, options, logger) =>
                {
                    (NetworkSocket serverConnection, Endpoint serverEndpoint) =
                        _serverNetworkSocketFactory(endpoint, options.TransportOptions, logger);

                    return endpoint.Protocol == Protocol.Ice1 ?
                        new Ice1Connection(serverEndpoint, serverConnection, options) :
                        new SlicConnection(serverEndpoint, serverConnection, options);
                };
            }
        }

        /// <summary>The transport enumerator.</summary>
        public Transport Transport { get; }

        private Func<Endpoint, ITransportOptions?, ILogger, NetworkSocket>? _clientNetworkSocketFactory;
        private Func<Endpoint, ITransportOptions?, ILogger, (NetworkSocket, Endpoint)>? _serverNetworkSocketFactory;

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
