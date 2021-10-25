// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Net.Security;

namespace IceRpc.Configure
{
    /// <summary>A composite server transport that creates multiplexed network connections.</summary>
    public class MultiplexedServerTransport : ServerTransport<IMultiplexedNetworkConnection>
    {
    }

    /// <summary>Extension methods for class <see cref="ServerTransport{IMultiplexedNetworkConnection}"/>.</summary>
    public static class MultiplexedServerTransportExtensions
    {
        /// <summary>Adds the coloc server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport<IMultiplexedNetworkConnection> UseColoc(
            this ServerTransport<IMultiplexedNetworkConnection> serverTransport) =>
            serverTransport.UseColoc(new());

        /// <summary>Adds the coloc server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport<IMultiplexedNetworkConnection> UseColoc(
            this ServerTransport<IMultiplexedNetworkConnection> serverTransport,
            SlicOptions options) =>
            serverTransport.Add(TransportNames.Coloc, new SlicServerTransport(new ColocServerTransport(), options));

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport<IMultiplexedNetworkConnection> UseTcp(
            this ServerTransport<IMultiplexedNetworkConnection> serverTransport) =>
            serverTransport.UseTcp(new TcpOptions(), new SlicOptions());

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport<IMultiplexedNetworkConnection> UseTcp(
            this ServerTransport<IMultiplexedNetworkConnection> serverTransport,
            TcpOptions tcpOptions,
            SlicOptions slicOptions) =>
            serverTransport.Add(TransportNames.Tcp,
                                new SlicServerTransport(new TcpServerTransport(tcpOptions, null), slicOptions));

        /// <summary>Adds the tcp server transport with ssl support to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport<IMultiplexedNetworkConnection> UseTcp(
            this ServerTransport<IMultiplexedNetworkConnection> serverTransport,
            SslServerAuthenticationOptions authenticationOptions) =>
            serverTransport.UseTcp(new TcpOptions(), new SlicOptions(), authenticationOptions);

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport<IMultiplexedNetworkConnection> UseTcp(
            this ServerTransport<IMultiplexedNetworkConnection> serverTransport,
            TcpOptions tcpOptions,
            SlicOptions slicOptions,
            SslServerAuthenticationOptions authenticationOptions) =>
            serverTransport.Add(
                TransportNames.Tcp,
                new SlicServerTransport(new TcpServerTransport(tcpOptions, authenticationOptions), slicOptions));
    }
}
