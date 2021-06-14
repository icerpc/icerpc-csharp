// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace IceRpc.Transports
{
    /// <summary>Registry for all transports known to this process.</summary>
    public static class TransportRegistry
    {
        private static readonly IDictionary<string, TransportDescriptor> _transportNameRegistry =
            new ConcurrentDictionary<string, TransportDescriptor>();

        private static readonly IDictionary<Transport, TransportDescriptor> _transportRegistry =
            new ConcurrentDictionary<Transport, TransportDescriptor>();

        /// <summary>Registers a new transport.</summary>
        /// <param name="descriptor">The transport descriptor.</param>
        public static void Add(TransportDescriptor descriptor)
        {
            if (descriptor.Name.Length == 0)
            {
                throw new ArgumentException($"{nameof(descriptor.Name)} cannot be empty", nameof(descriptor));
            }

            if (descriptor.Ice1EndpointFactory != null && descriptor.Ice1EndpointParser == null)
            {
                throw new ArgumentNullException($"{nameof(descriptor.Ice1EndpointParser)} cannot be null",
                                                nameof(descriptor));
            }

            if (descriptor.Ice1EndpointFactory == null && descriptor.Ice2EndpointParser == null)
            {
                throw new ArgumentNullException($"{nameof(descriptor.Ice2EndpointParser)} cannot be null",
                                                nameof(descriptor));
            }

            _transportRegistry.Add(descriptor.Transport, descriptor);
            _transportNameRegistry.Add(descriptor.Name, descriptor);

            if (descriptor.Ice2EndpointParser != null)
            {
                IceRpc.Internal.UriParser.RegisterTransport(descriptor.Name, descriptor.DefaultUriPort);
            }
        }

        internal static bool TryGetValue(
            Transport transport,
            [NotNullWhen(true)] out TransportDescriptor? descriptor) =>
            _transportRegistry.TryGetValue(transport, out descriptor);

        internal static bool TryGetValue(
            string name,
            [NotNullWhen(true)] out TransportDescriptor? descriptor) =>
            _transportNameRegistry.TryGetValue(name, out descriptor);

        static TransportRegistry()
        {
            Add(new TransportDescriptor(Transport.Coloc, "coloc", ColocEndpoint.CreateEndpoint)
                {
                    DefaultUriPort = ColocEndpoint.DefaultColocPort,
                    Ice1EndpointParser = ColocEndpoint.ParseIce1Endpoint,
                    Ice2EndpointParser = (host, port, _) => new ColocEndpoint(host, port, Protocol.Ice2),
                });

            Add(new TransportDescriptor(Transport.TCP, "tcp", TcpEndpoint.CreateEndpoint)
                {
                    DefaultUriPort = IPEndpoint.DefaultIPPort,
                    Ice1EndpointFactory = istr => TcpEndpoint.CreateIce1Endpoint(Transport.TCP, istr),
                    Ice1EndpointParser = (options, endpointString) =>
                        TcpEndpoint.ParseIce1Endpoint(Transport.TCP, options, endpointString),
                    Ice2EndpointParser = TcpEndpoint.ParseIce2Endpoint,
                });

            Add(new TransportDescriptor(Transport.SSL, "ssl", TcpEndpoint.CreateEndpoint)
                {
                    Ice1EndpointFactory = istr => TcpEndpoint.CreateIce1Endpoint(Transport.SSL, istr),
                    Ice1EndpointParser = (options, endpointString) =>
                        TcpEndpoint.ParseIce1Endpoint(Transport.SSL, options, endpointString),
                });

            Add(new TransportDescriptor(Transport.UDP, "udp", UdpEndpoint.CreateEndpoint)
                {
                    Ice1EndpointFactory = UdpEndpoint.CreateIce1Endpoint,
                    Ice1EndpointParser = UdpEndpoint.ParseIce1Endpoint,
                });

            Add(new TransportDescriptor(Transport.WS, "ws", WSEndpoint.CreateEndpoint)
                {
                    DefaultUriPort = IPEndpoint.DefaultIPPort,
                    Ice1EndpointFactory = istr => WSEndpoint.CreateIce1Endpoint(Transport.WS, istr),
                    Ice1EndpointParser = (options, endpointString) =>
                        WSEndpoint.ParseIce1Endpoint(Transport.WS, options, endpointString),
                    Ice2EndpointParser = WSEndpoint.ParseIce2Endpoint,
                });

            Add(new TransportDescriptor(Transport.WSS, "wss", WSEndpoint.CreateEndpoint)
                {
                    Ice1EndpointFactory = istr => WSEndpoint.CreateIce1Endpoint(Transport.WSS, istr),
                    Ice1EndpointParser = (options, endpointString) =>
                        WSEndpoint.ParseIce1Endpoint(Transport.WSS, options, endpointString),
                });
        }

        // See Runtime.UriInitialize
        internal static void UriInitialize()
        {
            if (_transportRegistry.Count == 0)
            {
                // should never happen
                throw new InvalidOperationException("transports are not yet registered");
            }
        }
    }
}
