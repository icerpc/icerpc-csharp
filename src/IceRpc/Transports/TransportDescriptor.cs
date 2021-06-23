// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IceRpc.Transports
{
    /// <summary>Holds properties for a transport, such as factory functions to create endpoints and connections.
    /// </summary>
    public class TransportDescriptor
    {
        /// <summary>The acceptor factory. An acceptor listens for connection establishment requests from clients and
        /// creates (accepts) a new connection for each client. This is typically used to implement a stream-based
        /// transport such as TCP or QUIC. Datagram or serial transports provide instead
        /// <see cref="IncomingConnectionFactory"/>.</summary>
        /// <seealso cref="ListeningSocketFactory"/>
        public Func<Endpoint, IncomingConnectionOptions, ILogger, IAcceptor>? AcceptorFactory { get; init; }

        /// <summary>The client socket factory. Setting this value sets <see cref="OutgoingConnectionFactory"/>. Set
        /// this value for a transport implemented using a <see cref="SingleStreamConnection"/>.</summary>
        public Func<Endpoint, ITransportOptions?, ILogger, SingleStreamConnection>? ClientSocketFactory
        {
            get => _clientSocketFactory;

            init
            {
                _clientSocketFactory = value;
                OutgoingConnectionFactory = _clientSocketFactory == null ? null :
                (endpoint, options, logger) =>
                {
                    SingleStreamConnection singleStreamConnection =
                        _clientSocketFactory(endpoint, options.TransportOptions, logger);

                    return endpoint.Protocol == Protocol.Ice1 ?
                        new Ice1Connection(endpoint, singleStreamConnection, options) :
                        new SlicConnection(endpoint, singleStreamConnection, options);
                };
            }
        }

        /// <summary>The listening socket factory. Setting this value sets <see cref="AcceptorFactory"/>. Set this value
        /// for a transport implemented using a <see cref="SingleStreamConnection"/> that provides an acceptor.
        /// </summary>
        public Func<Endpoint, ITransportOptions?, ILogger, (SingleStreamConnection, Endpoint)>? ListeningSocketFactory
        {
            get => _listeningSocketFactory;

            init
            {
                _listeningSocketFactory = value;
                AcceptorFactory = _listeningSocketFactory == null ? null :
                (endpoint, options, logger) =>
                {
                    (SingleStreamConnection listeningConnection, Endpoint listeningEndpoint) =
                        _listeningSocketFactory(endpoint, options.TransportOptions, logger);

                    return new SingleStreamConnectionAcceptor(listeningEndpoint, options, listeningConnection);
                };
            }
        }

        /// <summary>The server socket factory. Setting this value sets <see cref="IncomingConnectionFactory"/>. Set
        /// this value for a transport implemented using a <see cref="SingleStreamConnection"/> that does not provide an
        /// acceptor.</summary>
        public Func<Endpoint, ITransportOptions?, ILogger, (SingleStreamConnection, Endpoint)>? ServerSocketFactory
        {
            get => _serverSocketFactory;

            init
            {
                _serverSocketFactory = value;
                IncomingConnectionFactory = _serverSocketFactory == null ? null :
                (endpoint, options, logger) =>
                {
                    (SingleStreamConnection serverConnection, Endpoint serverEndpoint) =
                        _serverSocketFactory(endpoint, options.TransportOptions, logger);

                    return endpoint.Protocol == Protocol.Ice1 ?
                        new Ice1Connection(serverEndpoint, serverConnection, options) :
                        new SlicConnection(serverEndpoint, serverConnection, options);
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

        /// <summary>The incoming connection factory. It creates incoming connections that receive data from one or
        /// multiple clients. This factory is used to implement a transport that can only communicate with a single
        /// client (e.g. a serial based transport) or that can receive data from multiple clients with a single
        /// connection (e.g: UDP).</summary>
        /// <seealso cref="ServerSocketFactory"/>
        public Func<Endpoint, IncomingConnectionOptions, ILogger, MultiStreamConnection>? IncomingConnectionFactory { get; init; }

        /// <summary>The name of this transport in lower case, for example "tcp".</summary>
        public string Name { get; }

        /// <summary>The outgoing connection factory.</summary>
        /// <seealso cref="ClientSocketFactory"/>
        public Func<Endpoint, OutgoingConnectionOptions, ILogger, MultiStreamConnection>? OutgoingConnectionFactory { get; init; }

        /// <summary>The transport enumerator.</summary>
        public Transport Transport { get; }

        private Func<Endpoint, ITransportOptions?, ILogger, SingleStreamConnection>? _clientSocketFactory;
        private Func<Endpoint, ITransportOptions?, ILogger, (SingleStreamConnection, Endpoint)>? _listeningSocketFactory;
        private Func<Endpoint, ITransportOptions?, ILogger, (SingleStreamConnection, Endpoint)>? _serverSocketFactory;

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
