// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Configure
{
    /// <summary>A composite server transport that creates multiplexed network connections.</summary>
    public class CompositeMultiplexedServerTransport : CompositeServerTransport<IMultiplexedNetworkConnection>
    {
    }

    /// <summary>Extension methods for class <see cref="CompositeServerTransport{IMultiplexedNetworkConnection}"/>.
    /// </summary>
    public static class CompositeMultiplexedServerTransportExtensions
    {
        /// <summary>Adds the Slic over TCP server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The composite transport being configured.</param>
        /// <returns>The composite transport being configured.</returns>
        public static CompositeServerTransport<IMultiplexedNetworkConnection> UseSlicOverTcp(
            this CompositeServerTransport<IMultiplexedNetworkConnection> serverTransport) =>
            serverTransport.Add(new SlicServerTransport(new TcpServerTransport()));

        /// <summary>Adds a Slic server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The composite transport being configured.</param>
        /// <param name="options">The Slic server transport options.</param>
        /// <returns>The composite transport being configured.</returns>
        public static CompositeServerTransport<IMultiplexedNetworkConnection> UseSlic(
            this CompositeServerTransport<IMultiplexedNetworkConnection> serverTransport,
            SlicServerTransportOptions options) => serverTransport.Add(new SlicServerTransport(options));
    }
}
