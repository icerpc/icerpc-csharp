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
        private static readonly IDictionary<string, ITransportDescriptor> _transportNameRegistry =
            new ConcurrentDictionary<string, ITransportDescriptor>();

        private static readonly IDictionary<Transport, ITransportDescriptor> _transportRegistry =
            new ConcurrentDictionary<Transport, ITransportDescriptor>();

        /// <summary>Registers a new transport.</summary>
        /// <param name="descriptor">The transport descriptor.</param>
        public static void Add(ITransportDescriptor descriptor)
        {
            if (descriptor.Name.Length == 0)
            {
                throw new ArgumentException($"{nameof(descriptor.Name)} cannot be empty", nameof(descriptor));
            }

            _transportRegistry.Add(descriptor.Transport, descriptor);
            _transportNameRegistry.Add(descriptor.Name, descriptor);

            if (descriptor is IIce2TransportDescriptor ice2Descriptor)
            {
                IceRpc.Internal.UriParser.RegisterTransport(descriptor.Name, ice2Descriptor.DefaultUriPort);
            }
        }

        internal static bool TryGetValue(
            Transport transport,
            [NotNullWhen(true)] out ITransportDescriptor? descriptor) =>
            _transportRegistry.TryGetValue(transport, out descriptor);

        internal static bool TryGetValue(
            string name,
            [NotNullWhen(true)] out ITransportDescriptor? descriptor) =>
            _transportNameRegistry.TryGetValue(name, out descriptor);

        static TransportRegistry()
        {
            Add(ColocEndpoint.TransportDescriptor);
            Add(TcpEndpoint.GetTransportDescriptor(Transport.TCP));
            Add(TcpEndpoint.GetTransportDescriptor(Transport.SSL));
            Add(UdpEndpoint.TransportDescriptor);
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
