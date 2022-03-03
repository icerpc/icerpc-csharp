// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;

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
        /// <param name="serverTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeServerTransport<IMultiplexedNetworkConnection> UseSlicOverTcp(
            this CompositeServerTransport<IMultiplexedNetworkConnection> serverTransport) =>
            serverTransport.Add(TransportNames.Tcp, new SlicServerTransport(new TcpServerTransport()));
    }
}
