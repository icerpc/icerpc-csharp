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
        /// <summary>Adds the coloc server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeServerTransport<IMultiplexedNetworkConnection> UseColoc(
            this CompositeServerTransport<IMultiplexedNetworkConnection> serverTransport) =>
            serverTransport.UseColoc(new());

        /// <summary>Adds the coloc server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeServerTransport<IMultiplexedNetworkConnection> UseColoc(
            this CompositeServerTransport<IMultiplexedNetworkConnection> serverTransport,
            SlicOptions options) =>
            serverTransport.Add(TransportNames.Coloc, new SlicServerTransport(new ColocServerTransport(), options));

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeServerTransport<IMultiplexedNetworkConnection> UseTcp(
            this CompositeServerTransport<IMultiplexedNetworkConnection> serverTransport) =>
            serverTransport.UseTcp(new TcpServerOptions(), new SlicOptions());

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP server options.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeServerTransport<IMultiplexedNetworkConnection> UseTcp(
            this CompositeServerTransport<IMultiplexedNetworkConnection> serverTransport,
            TcpServerOptions tcpOptions,
            SlicOptions slicOptions) =>
            serverTransport.Add(TransportNames.Tcp,
                                new SlicServerTransport(new TcpServerTransport(tcpOptions), slicOptions));
    }
}
