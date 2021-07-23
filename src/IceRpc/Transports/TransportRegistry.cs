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
        private static readonly IDictionary<string, IEndpointFactory> _transportNameRegistry =
            new ConcurrentDictionary<string, IEndpointFactory>();

        private static readonly IDictionary<TransportCode, IEndpointFactory> _transportRegistry =
            new ConcurrentDictionary<TransportCode, IEndpointFactory>();

        /// <summary>Registers a new transport.</summary>
        /// <param name="factory">The endpoint factory.</param>
        public static void Add(IEndpointFactory factory)
        {
            if (factory.Name.Length == 0)
            {
                throw new ArgumentException($"{nameof(factory.Name)} cannot be empty", nameof(factory));
            }

            _transportRegistry.Add(factory.TransportCode, factory);
            _transportNameRegistry.Add(factory.Name, factory);

            if (factory is IIce2EndpointFactory ice2Descriptor)
            {
                IceRpc.Internal.IceUriParser.RegisterTransport(factory.Name, ice2Descriptor.DefaultUriPort);
            }
        }

        internal static bool TryGetValue(
            TransportCode transport,
            [NotNullWhen(true)] out IEndpointFactory? factory) =>
            _transportRegistry.TryGetValue(transport, out factory);

        internal static bool TryGetValue(
            string name,
            [NotNullWhen(true)] out IEndpointFactory? factory) =>
            _transportNameRegistry.TryGetValue(name, out factory);

        static TransportRegistry()
        {
            Add(new ColocEndpointFactory());
            Add(new TcpEndpointFactory());
            Add(new SslEndpointFactory());
            Add(new UdpEndpointFactory());
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
