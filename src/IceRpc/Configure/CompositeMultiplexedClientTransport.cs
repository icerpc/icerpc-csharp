// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Configure
{
    /// <summary>A composite client transport that creates multiplexed network connections.</summary>
    public class CompositeMultiplexedClientTransport : CompositeClientTransport<IMultiplexedNetworkConnection>
    {
    }

    /// <summary>Extension methods for class <see cref="CompositeClientTransport{IMultiplexedNetworkConnection}"/>.
    /// </summary>
    public static class CompositeMultiplexedClientTransportExtensions
    {
        /// <summary>Adds the Slic over TCP client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The composite transport being configured.</param>
        /// <returns>The composite transport being configured.</returns>
        public static CompositeClientTransport<IMultiplexedNetworkConnection> UseSlicOverTcp(
            this CompositeClientTransport<IMultiplexedNetworkConnection> clientTransport) =>
            clientTransport.Add(new SlicClientTransport(new TcpClientTransport()));

        /// <summary>Adds a Slic transport to this composite client transport.</summary>
        /// <param name="clientTransport">The composite transport being configured.</param>
        /// <param name="options">The Slic client transport options.</param>
        /// <returns>The composite transport being configured.</returns>
        public static CompositeClientTransport<IMultiplexedNetworkConnection> UseSlic(
            this CompositeClientTransport<IMultiplexedNetworkConnection> clientTransport,
            SlicClientTransportOptions options) => clientTransport.Add(new SlicClientTransport(options));
    }
}
