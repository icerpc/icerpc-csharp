// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace IceRpc.Transports
{
    /// <summary>Keeps track of all transports known to this process.</summary>
    public static class TransportRegistry
    {
        private static readonly IDictionary<string, (Transport Transport, TransportDescriptor Descriptor)> _transportNameRegistry =
            new ConcurrentDictionary<string, (Transport, TransportDescriptor)>();

        private static readonly IDictionary<Transport, TransportDescriptor> _transportRegistry =
            new ConcurrentDictionary<Transport, TransportDescriptor>();

        /// <summary>Registers a new transport.</summary>
        /// <param name="transport">The transport.</param>
        /// <param name="descriptor">The transport descriptor.</param>
        public static void Add(Transport transport, TransportDescriptor descriptor)
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

            _transportRegistry.Add(transport, descriptor);
            _transportNameRegistry.Add(descriptor.Name, (transport, descriptor));

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
            out (Transport Transport, TransportDescriptor Descriptor) value) =>
            _transportNameRegistry.TryGetValue(name, out value);

        static TransportRegistry()
        {
            Add(Transport.Coloc,
                new TransportDescriptor("coloc", ColocEndpoint.CreateEndpoint)
                {
                    DefaultUriPort = ColocEndpoint.DefaultColocPort,
                    Ice1EndpointParser = ColocEndpoint.ParseIce1Endpoint,
                    Ice2EndpointParser = (host, port, _) => new ColocEndpoint(host, port, Protocol.Ice2),
                });

            Add(Transport.TCP,
                new TransportDescriptor("tcp", TcpEndpoint.CreateEndpoint)
                {
                    DefaultUriPort = IPEndpoint.DefaultIPPort,
                    Ice1EndpointFactory = istr => TcpEndpoint.CreateIce1Endpoint(Transport.TCP, istr),
                    Ice1EndpointParser = (options, endpointString) =>
                        TcpEndpoint.ParseIce1Endpoint(Transport.TCP, options, endpointString),
                    Ice2EndpointParser = TcpEndpoint.ParseIce2Endpoint,
                });

            Add(Transport.SSL,
                new TransportDescriptor("ssl", TcpEndpoint.CreateEndpoint)
                {
                    Ice1EndpointFactory = istr => TcpEndpoint.CreateIce1Endpoint(Transport.SSL, istr),
                    Ice1EndpointParser = (options, endpointString) =>
                        TcpEndpoint.ParseIce1Endpoint(Transport.SSL, options, endpointString),
                });

            Add(Transport.UDP,
                new TransportDescriptor("udp", UdpEndpoint.CreateEndpoint)
                {
                    Ice1EndpointFactory = UdpEndpoint.CreateIce1Endpoint,
                    Ice1EndpointParser = UdpEndpoint.ParseIce1Endpoint,
                });

            Add(Transport.WS,
                new TransportDescriptor("ws", WSEndpoint.CreateEndpoint)
                {
                    DefaultUriPort = IPEndpoint.DefaultIPPort,
                    Ice1EndpointFactory = istr => WSEndpoint.CreateIce1Endpoint(Transport.WS, istr),
                    Ice1EndpointParser = (options, endpointString) =>
                        WSEndpoint.ParseIce1Endpoint(Transport.WS, options, endpointString),
                    Ice2EndpointParser = WSEndpoint.ParseIce2Endpoint,
                });

            Add(Transport.WSS,
                new TransportDescriptor("wss", WSEndpoint.CreateEndpoint)
                {
                    Ice1EndpointFactory = istr => WSEndpoint.CreateIce1Endpoint(Transport.WSS, istr),
                    Ice1EndpointParser = (options, endpointString) =>
                        WSEndpoint.ParseIce1Endpoint(Transport.WSS, options, endpointString),
                });
        }

        // Must be called before parsing a Uri to make sure Runtime's static constructor executed and registered the
        // URI schemes for the built-in transports.
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
