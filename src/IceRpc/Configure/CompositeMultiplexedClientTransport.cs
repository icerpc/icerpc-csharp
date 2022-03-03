// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;

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
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeClientTransport<IMultiplexedNetworkConnection> UseSlicOverTcp(
            this CompositeClientTransport<IMultiplexedNetworkConnection> clientTransport) =>
            clientTransport.Add(TransportNames.Tcp, new SlicClientTransport(new TcpClientTransport()));
    }
}
